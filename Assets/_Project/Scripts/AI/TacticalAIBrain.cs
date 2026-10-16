using System;
using FPS.Ballistics;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace FPS.AI
{
    public enum AIState
    {
        Idle,
        Investigate,
        Engage,
        Suppress,
        Flank,
        Stack,
        Breach,
        Throw,
        Stunned,
        Dead,
        /// <summary>Moving to, holding or working a match objective.</summary>
        Objective,
    }

    public enum ObjectiveKind
    {
        None,
        /// <summary>Go to the point and hold it, fighting from there.</summary>
        Hold,
        /// <summary>Go to the point and work it (plant, defuse) whenever no enemy is in sight.</summary>
        Interact,
    }

    /// <summary>
    /// One soldier's tactics. Unaware soldiers hold or patrol; suspicious ones turn to look; alerted ones move up on the
    /// last known position, pre-aiming around corners (slicing the pie). In a firefight they take cover against the
    /// threat and fight from it in peek cycles — standing up over low cover or leaning out of tall cover — with reaction
    /// time, tracking and suppression-dependent aim error. When contact breaks they keep suppressive fire on the last
    /// known position, then hunt it; a target sitting tight behind cover gets a frag. Squad orders (flank, stack on a
    /// door, breach and dominate a room, throw a flashbang) come from the <see cref="SquadCoordinator"/>. Every key
    /// action is shouted.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AIBody), typeof(AIPerceptionSensor), typeof(AIWeaponHandler))]
    public sealed class TacticalAIBrain : MonoBehaviour
    {
        private const int MaxRoute = 8;
        private const int MaxClaims = 8;
        private static readonly int CalloutCount = Enum.GetValues(typeof(CalloutType)).Length;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 1.5f;
        [SerializeField, Min(0.1f)] private float runSpeed = 3.8f;
        [SerializeField, Min(0.1f)] private float crouchSpeed = 1f;
        [SerializeField, Min(0.1f)] private float arriveDistance = 0.6f;

        [Header("Firing")]
        [Tooltip("Delay between identifying a target and the first shot (s).")]
        [SerializeField] private Vector2 reactionTime = new Vector2(0.35f, 0.7f);
        [SerializeField] private Vector2Int burstLength = new Vector2Int(3, 6);
        [SerializeField] private Vector2 burstPause = new Vector2(0.3f, 0.8f);
        [Tooltip("Aim error of a settled, unstressed soldier (deg).")]
        [SerializeField, Min(0f)] private float aimSpread = 1.2f;
        [Tooltip("Aim error multiplier added at full suppression.")]
        [SerializeField, Min(0f)] private float suppressedSpread = 3f;
        [Tooltip("Extra error right after acquiring a target, settling over about a second.")]
        [SerializeField, Min(0f)] private float acquisitionSpread = 1.5f;
        [Tooltip("The trigger is only pulled with the bore within this of the aim point (deg).")]
        [SerializeField, Range(0.5f, 20f)] private float aimTolerance = 4f;
        [SerializeField, Min(1f)] private float maxEngageRange = 150f;

        [Header("Cover")]
        [SerializeField, Min(2f)] private float coverSearchRadius = 14f;
        [SerializeField, Min(0f)] private float minThreatDistance = 4f;
        [Tooltip("Inside this range soldiers fight where they stand instead of moving to cover (m).")]
        [SerializeField, Min(0f)] private float closeQuarters = 7f;
        [SerializeField, Range(0f, 1f)] private float coverSuppressionThreshold = 0.25f;
        [SerializeField] private Vector2 peekTime = new Vector2(1.5f, 3f);
        [SerializeField] private Vector2 hideTime = new Vector2(0.8f, 2f);

        [Header("Suppressive Fire")]
        [Tooltip("How long after losing sight the last known position stays under fire (s).")]
        [SerializeField, Min(0f)] private float suppressWindow = 6f;
        [SerializeField, Min(0f)] private float suppressSpreadRadius = 1.2f;
        [SerializeField] private Vector2 suppressPause = new Vector2(0.5f, 1.2f);

        [Header("Grenades")]
        [SerializeField] private GrenadeDataSO fragGrenade;
        [SerializeField, Min(0)] private int fragCount = 1;
        [SerializeField] private GrenadeDataSO flashGrenade;
        [SerializeField, Min(0)] private int flashCount = 1;
        [Tooltip("A target that stays hidden within 1.5 m of one spot this long is flushed with a frag (s).")]
        [SerializeField, Min(0f)] private float entrenchTime = 4f;
        [SerializeField] private Vector2 grenadeRange = new Vector2(6f, 28f);
        [SerializeField, Min(0.1f)] private float throwTime = 0.7f;

        [Header("Voice")]
        [SerializeField] private AIVoiceProfileSO voice;
        [SerializeField, Min(0f)] private float calloutCooldown = 2.5f;

        [Header("Patrol & Objectives")]
        [SerializeField] private Transform[] patrolPoints = new Transform[0];
        [Tooltip("Threats further than this from the objective are watched, not chased (m).")]
        [SerializeField, Min(1f)] private float objectiveLeash = 25f;

        private readonly Vector3[] route = new Vector3[MaxRoute];
        private readonly Vector3[] claims = new Vector3[MaxClaims];
        private float[] lastCallout;
        private AIBody body;
        private AIPerceptionSensor perception;
        private AIWeaponHandler weapon;
        private NavMeshAgent agent;
        private TacticalHealthAndArmorSystem health;
        private AudioSource voiceSource;

        private float nextDecision, fireAllowedAt, nextBurstAt, phaseEnd, lastCoverSearch, trackingSince, throwEnd;
        private bool peeking, hasCover;
        private CoverPoint cover;
        private Vector3 suppressPoint, entrenchAnchor, orderLook, lastHitDirection = Vector3.forward;
        private float entrenchSince, coveringUntil, noiseSeed;
        private int routeCount, routeIndex, arcCount, patrolIndex;
        private Transform breachEntry;
        private AIState resumeState;
        private ObjectiveKind objectiveKind;
        private Vector3 objectivePoint, objectiveFacing;
        private float objectiveRadius;

        public AIState State { get; private set; }
        public SquadCoordinator Squad { get; private set; }
        public AIBody Body => body;
        public AIPerceptionSensor Perception => perception;
        public AIWeaponHandler WeaponHandler => weapon;
        public bool HasCover => hasCover;
        public CoverPoint Cover => cover;
        public bool AtCover => hasCover && Arrived(cover.Position);
        public bool IsPeeking => peeking;
        public bool IsDead => State == AIState.Dead;
        public ThrownGrenade LastGrenade { get; private set; }
        public int FragGrenades => fragCount;
        public int FlashGrenades => flashCount;
        public Vector3 SuppressPoint => suppressPoint;

        /// <summary>True when the current squad order has been carried out.</summary>
        public bool IsOrderComplete
        {
            get
            {
                switch (State)
                {
                    case AIState.Flank:
                    case AIState.Stack: return Arrived(route[0]);
                    case AIState.Breach: return routeIndex >= routeCount;
                    default: return true;
                }
            }
        }

        public ObjectiveKind Objective => objectiveKind;
        public Vector3 ObjectivePoint => objectivePoint;
        public bool AtObjective => objectiveKind != ObjectiveKind.None && Arrived(objectivePoint, objectiveRadius);
        /// <summary>At an Interact objective with no enemy in sight: the match counts this as planting or defusing.</summary>
        public bool IsWorkingObjective => State == AIState.Objective && objectiveKind == ObjectiveKind.Interact && AtObjective &&
                                          !perception.TargetVisible && !perception.IsStunned;

        /// <summary>Breaching and already through the doorway (the next man may follow).</summary>
        public bool PastDoorway => State == AIState.Breach && routeIndex > arcCount;

        public void Configure(AIVoiceProfileSO voiceProfile, GrenadeDataSO frag, int frags, GrenadeDataSO flash, int flashes)
        {
            voice = voiceProfile;
            fragGrenade = frag;
            fragCount = frags;
            flashGrenade = flash;
            flashCount = flashes;
        }

        public void JoinSquad(SquadCoordinator coordinator) => Squad = coordinator;

        /// <summary>A standing match objective, pursued whenever there is no fight to take or order to follow.</summary>
        public void SetObjective(ObjectiveKind kind, Vector3 point, float radius, Vector3 facing)
        {
            objectiveKind = kind;
            objectivePoint = OnNavMesh(point);
            objectiveRadius = Mathf.Max(0.5f, radius);
            objectiveFacing = facing;
            nextDecision = 0f;
        }

        public void ClearObjective()
        {
            objectiveKind = ObjectiveKind.None;
            if (State == AIState.Objective) SetState(AIState.Idle);
        }

        public bool HasGrenade(GrenadeType type) =>
            type == GrenadeType.Fragmentation ? fragGrenade != null && fragCount > 0 : flashGrenade != null && flashCount > 0;

        // ---- Squad orders ----

        public void OrderFlank(Vector3 point, bool left)
        {
            if (IsDead) return;
            SetState(AIState.Flank);
            route[0] = OnNavMesh(point);
            routeCount = 1;
            routeIndex = 0;
            Callout(left ? CalloutType.FlankingLeft : CalloutType.FlankingRight);
        }

        public void OrderStack(Vector3 point, Vector3 lookAt)
        {
            if (IsDead) return;
            SetState(AIState.Stack);
            route[0] = OnNavMesh(point);
            routeCount = 1;
            routeIndex = 0;
            orderLook = lookAt;
        }

        /// <summary>Slice the pie along <paramref name="arc"/>, go through <paramref name="entry"/> and take <paramref name="domination"/>.</summary>
        public void OrderBreach(Transform entry, Vector3[] arc, int count, Vector3 domination)
        {
            if (IsDead) return;
            SetState(AIState.Breach);
            breachEntry = entry;
            arcCount = Mathf.Min(count, MaxRoute - 2);
            for (int i = 0; i < arcCount; i++) route[i] = OnNavMesh(arc[i]);
            route[arcCount] = OnNavMesh(entry.position);
            route[arcCount + 1] = OnNavMesh(domination);
            routeCount = arcCount + 2;
            routeIndex = 0;
        }

        /// <summary>Throws a grenade of <paramref name="type"/> to land at <paramref name="target"/>. False when not possible.</summary>
        public bool OrderThrow(GrenadeType type, Vector3 target, bool preferHighArc)
        {
            if (IsDead || perception.IsStunned) return false;
            return TryThrow(type == GrenadeType.Fragmentation ? fragGrenade : flashGrenade, type, target, preferHighArc);
        }

        public void ReleaseOrders()
        {
            if (State == AIState.Flank || State == AIState.Stack || State == AIState.Breach) SetState(AIState.Idle);
            nextDecision = 0f;
        }

        /// <summary>Lay heavier fire on the target for a while (a teammate is reloading).</summary>
        public void ProvideCoveringFire(float duration) => coveringUntil = Time.time + duration;

        /// <summary>Shouts <paramref name="type"/> (rate-limited per callout, deduplicated across the squad).</summary>
        public void Callout(CalloutType type)
        {
            float now = Time.time;
            if (now - lastCallout[(int)type] < calloutCooldown) return;
            if (Squad != null && !Squad.TryClaimCallout(type)) return;
            lastCallout[(int)type] = now;

            AudioClip clip = voice != null ? voice.Pick(type) : null;
            if (clip != null) voiceSource.PlayOneShot(clip);
            EventBus<SquadCalloutEvent>.Raise(new SquadCalloutEvent { Speaker = transform, Type = type, Position = body.Head.position });
        }

        private void Awake()
        {
            body = GetComponent<AIBody>();
            perception = GetComponent<AIPerceptionSensor>();
            weapon = GetComponent<AIWeaponHandler>();
            agent = GetComponent<NavMeshAgent>();
            TryGetComponent(out health);
            lastCallout = new float[CalloutCount];
            for (int i = 0; i < CalloutCount; i++) lastCallout[i] = float.NegativeInfinity;
            noiseSeed = Random.Range(0f, 100f);

            if (!body.Head.TryGetComponent(out voiceSource)) voiceSource = body.Head.gameObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 1f;
            voiceSource.rolloffMode = AudioRolloffMode.Linear;
            voiceSource.minDistance = 2f;
            voiceSource.maxDistance = voice != null ? voice.AudibleDistance : 45f;
            voiceSource.dopplerLevel = 0f;
        }

        private void OnEnable()
        {
            perception.TargetIdentified += OnTargetIdentified;
            weapon.ReloadStarted += OnReloadStarted;
            if (health != null) health.Died += OnDied;
            EventBus<DamageEvent>.Subscribe(OnDamage);
        }

        private void OnDisable()
        {
            perception.TargetIdentified -= OnTargetIdentified;
            weapon.ReloadStarted -= OnReloadStarted;
            if (health != null) health.Died -= OnDied;
            EventBus<DamageEvent>.Unsubscribe(OnDamage);
        }

        private void Update()
        {
            if (IsDead) return;
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + Random.Range(0.15f, 0.25f);
                Decide();
            }

            Act();
        }

        // ---- Decisions (a few times a second) ----

        private void Decide()
        {
            float now = Time.time;
            if (perception.IsStunned)
            {
                SetState(AIState.Stunned);
                return;
            }

            if (State == AIState.Stunned) SetState(AIState.Idle);
            if (State == AIState.Throw)
            {
                if (now < throwEnd) return;
                SetState(resumeState); // back to the squad order the throw interrupted
                if (resumeState != AIState.Idle) return;
            }

            UpdateEntrenchment(now);
            ConsiderReload();

            if (State == AIState.Flank)
            {
                if (perception.TargetVisible || Arrived(route[0]))
                {
                    Squad?.OnFlankFinished(this);
                    SetState(AIState.Engage);
                }

                return;
            }

            if (State == AIState.Stack || State == AIState.Breach) return;

            switch (perception.State)
            {
                case AwarenessState.Unaware:
                case AwarenessState.Suspicious:
                    SetState(objectiveKind != ObjectiveKind.None ? AIState.Objective : AIState.Idle);
                    break;
                case AwarenessState.Alerted:
                    SetState(BeyondLeash(perception.LastKnownPosition) ? AIState.Objective : AIState.Investigate);
                    break;
                default:
                    DecideCombat(now);
                    break;
            }
        }

        /// <summary>A threat this far from the held objective is watched from it rather than hunted.</summary>
        private bool BeyondLeash(Vector3 point) =>
            objectiveKind != ObjectiveKind.None && (point - objectivePoint).sqrMagnitude > objectiveLeash * objectiveLeash;

        private void DecideCombat(float now)
        {
            if (perception.TargetVisible)
            {
                SetState(AIState.Engage);
                DecideCover(now);
                return;
            }

            if (TryFlushEntrenched(now)) return;

            if (perception.TimeSinceSeen < suppressWindow || now < coveringUntil)
            {
                if (State != AIState.Suppress) suppressPoint = PickSuppressPoint();
                SetState(AIState.Suppress);
                DecideCover(now);
                return;
            }

            SetState(BeyondLeash(perception.LastKnownPosition) ? AIState.Objective : AIState.Investigate);
            hasCover = false;
        }

        private void DecideCover(float now)
        {
            PerceivableTarget target = perception.Target;
            Vector3 threatEye = target != null && perception.TargetVisible ? target.Head.position : perception.LastKnownPosition + Vector3.up * 0.3f;
            float distance = Vector3.Distance(transform.position, threatEye);

            if (hasCover)
            {
                if (CoverFinder.Evaluate(cover.Position, threatEye, out PeekMode peek, out bool tall))
                {
                    cover.Peek = peek;
                    cover.Tall = tall;
                    return;
                }

                hasCover = false; // the threat moved round it
            }

            bool wantCover = distance > closeQuarters &&
                             (perception.Suppression > coverSuppressionThreshold || distance > 12f ||
                              (health != null && health.Health < health.MaxHealth * 0.7f));
            if (!wantCover || now - lastCoverSearch < 1.5f) return;

            lastCoverSearch = now;
            int claimCount = Squad != null ? Squad.FillCoverClaims(this, claims) : 0;
            if (!CoverFinder.Find(transform.position, threatEye, coverSearchRadius, minThreatDistance, claims, claimCount, out CoverPoint found))
                return;

            cover = found;
            hasCover = true;
            peeking = false;
            phaseEnd = 0f;
            Squad?.ClaimCover(this, cover.Position);
        }

        private void ConsiderReload()
        {
            if (weapon.IsReloading || weapon.Data == null) return;
            bool empty = weapon.IsEmpty || weapon.LoadedRounds <= 0;
            bool low = weapon.LoadedRounds < weapon.Capacity * 0.3f && !perception.TargetVisible;
            if (empty || low) weapon.StartReload();
        }

        private void UpdateEntrenchment(float now)
        {
            Vector3 lkp = perception.LastKnownPosition;
            if (perception.TargetVisible || (lkp - entrenchAnchor).sqrMagnitude > 1.5f * 1.5f)
            {
                entrenchAnchor = lkp;
                entrenchSince = now;
            }
        }

        private bool TryFlushEntrenched(float now)
        {
            if (fragGrenade == null || fragCount <= 0 || now - entrenchSince < entrenchTime) return false;
            if (perception.TimeSinceInfo > suppressWindow * 2f) return false; // stale: not worth a grenade

            Vector3 lkp = perception.LastKnownPosition;
            float distance = Vector3.Distance(transform.position, lkp);
            if (distance < grenadeRange.x || distance > grenadeRange.y) return false;
            if (Squad != null && !Squad.RequestGrenade(this)) return false;

            return TryThrow(fragGrenade, GrenadeType.Fragmentation, lkp, true);
        }

        private bool TryThrow(GrenadeDataSO grenade, GrenadeType type, Vector3 target, bool preferHighArc)
        {
            if (grenade == null || grenade.Prefab == null || (type == GrenadeType.Fragmentation ? fragCount : flashCount) <= 0) return false;

            Vector3 origin = body.Head.position + body.transform.right * 0.15f + Vector3.up * 0.15f;
            Vector3 landing = GroundBelow(target);
            if (!SolveClearThrow(origin, landing, grenade.ThrowSpeed, preferHighArc, out Vector3 velocity, out float flight) &&
                !SolveClearThrow(origin, landing, grenade.ThrowSpeed, !preferHighArc, out velocity, out flight))
                return false;

            weapon.CeaseFire();
            // Cook a frag so it goes off about a second after landing: no time to throw it back.
            float cook = type == GrenadeType.Fragmentation ? Mathf.Clamp(grenade.FuseTime - flight - 1f, 0f, 2f) : 0f;
            ThrownGrenade live = ObjectPoolManager.Instance.Spawn(grenade.Prefab, origin, Quaternion.LookRotation(velocity));
            live.Throw(grenade, transform, velocity, cook);
            LastGrenade = live;
            if (type == GrenadeType.Fragmentation) fragCount--;
            else flashCount--;

            Callout(type == GrenadeType.Fragmentation ? CalloutType.FragOut : CalloutType.FlashbangOut);
            resumeState = State == AIState.Stack || State == AIState.Breach || State == AIState.Flank ? State : AIState.Idle;
            SetState(AIState.Throw);
            throwEnd = Time.time + throwTime;
            entrenchSince = Time.time;
            return true;
        }

        private static bool SolveClearThrow(Vector3 origin, Vector3 landing, float speed, bool highArc, out Vector3 velocity, out float flight)
        {
            flight = 0f;
            if (!GrenadeBallistics.SolveLaunch(origin, landing, speed, highArc, out velocity)) return false;
            flight = GrenadeBallistics.FlightTime(origin, landing, velocity);
            return GrenadeBallistics.IsArcClear(origin, velocity, flight, 0.05f, GameLayers.EnvironmentMask);
        }

        private static Vector3 GroundBelow(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * 0.1f
                : point - Vector3.up * 1.2f;
        }

        private Vector3 PickSuppressPoint()
        {
            Vector2 offset = Random.insideUnitCircle * suppressSpreadRadius;
            return perception.LastKnownPosition + new Vector3(offset.x, Random.Range(-0.4f, 0.4f), offset.y);
        }

        // ---- Actions (every frame) ----

        private void Act()
        {
            switch (State)
            {
                case AIState.Idle: ActIdle(); break;
                case AIState.Investigate: ActInvestigate(); break;
                case AIState.Engage: ActFight(false); break;
                case AIState.Suppress: ActFight(true); break;
                case AIState.Flank: ActFlank(); break;
                case AIState.Stack: ActStack(); break;
                case AIState.Breach: ActBreach(); break;
                case AIState.Objective: ActObjective(); break;
                case AIState.Throw:
                    Stop();
                    body.LookAt(perception.LastKnownPosition);
                    break;
                case AIState.Stunned:
                    Stop();
                    weapon.CeaseFire();
                    body.SetCrouched(true);
                    body.SetLean(0f);
                    body.ClearAim();
                    break;
            }
        }

        private void ActIdle()
        {
            body.SetCrouched(false);
            body.SetLean(0f);
            if (perception.State == AwarenessState.Suspicious)
            {
                Stop();
                body.LookAt(perception.LastKnownPosition);
                return;
            }

            body.ClearAim();
            if (patrolPoints.Length == 0)
            {
                Stop();
                return;
            }

            Vector3 post = patrolPoints[patrolIndex].position;
            if (Arrived(post)) patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            MoveTo(patrolPoints[patrolIndex].position, walkSpeed);
        }

        private void ActObjective()
        {
            body.SetLean(0f);
            Vector3 position = transform.position;
            bool arrived = Arrived(objectivePoint, objectiveRadius);
            if (!arrived)
            {
                body.SetCrouched(false);
                MoveTo(objectivePoint, (objectivePoint - position).sqrMagnitude > 12f * 12f ? runSpeed : walkSpeed);
            }
            else
            {
                Stop();
                body.SetCrouched(objectiveKind == ObjectiveKind.Interact);
            }

            if (perception.TargetVisible)
            {
                EngageTarget(!arrived);
                return;
            }

            weapon.CeaseFire();
            if (perception.State == AwarenessState.Alerted || perception.State == AwarenessState.Engaged) body.AimAt(perception.LastKnownPosition);
            else if (arrived) body.LookAt(objectivePoint + objectiveFacing * 10f + Vector3.up * 1.5f);
            else body.ClearAim();
        }

        private void ActInvestigate()
        {
            body.SetLean(0f);
            body.SetCrouched(false);
            Vector3 lkp = perception.LastKnownPosition;
            MoveTo(lkp, walkSpeed);
            body.AimAt(PreAimPoint(lkp));
            if (perception.TargetVisible) EngageTarget(false);
        }

        /// <summary>Slicing the pie at corners: aim past the next turn of the path toward the last known position.</summary>
        private Vector3 PreAimPoint(Vector3 lkp)
        {
            Vector3 aim = lkp + Vector3.up * 0.2f;
            if (!agent.enabled || !agent.hasPath) return aim;

            Vector3 corner = agent.steeringTarget;
            Vector3 position = transform.position;
            if ((corner - agent.destination).sqrMagnitude < 0.25f || (corner - position).sqrMagnitude > 4f * 4f) return aim;

            Vector3 beyond = lkp - corner;
            beyond.y = 0f;
            if (beyond.sqrMagnitude < 1e-3f) return aim;
            return corner + beyond.normalized * 3f + Vector3.up * 1.3f;
        }

        private void ActFight(bool suppressing)
        {
            PerceivableTarget target = perception.Target;
            bool visible = perception.TargetVisible && target != null;
            if (!hasCover)
            {
                Stop();
                float distance = visible ? Vector3.Distance(transform.position, target.AimPoint) : 0f;
                body.SetCrouched(perception.Suppression > coverSuppressionThreshold || distance > 15f);
                body.SetLean(0f);
                if (visible) EngageTarget(false);
                else SuppressFire();
                return;
            }

            if (!AtCover)
            {
                // Rush to cover; snap shots only when the threat is close and in sight.
                body.SetCrouched(false);
                body.SetLean(0f);
                MoveTo(cover.Position, runSpeed);
                if (visible && Vector3.Distance(transform.position, target.AimPoint) < 20f) EngageTarget(true);
                else
                {
                    weapon.CeaseFire();
                    body.AimAt(visible ? target.AimPoint : perception.LastKnownPosition);
                }

                return;
            }

            Stop();
            float now = Time.time;
            if (now >= phaseEnd && !weapon.IsReloading)
            {
                peeking = !peeking && cover.Peek != PeekMode.None;
                phaseEnd = now + (peeking ? Random.Range(peekTime.x, peekTime.y) : Random.Range(hideTime.x, hideTime.y));
            }

            if (weapon.IsReloading) peeking = false;
            if (!peeking)
            {
                body.SetCrouched(!cover.Tall);
                body.SetLean(0f);
                weapon.CeaseFire();
                body.AimAt(visible ? target.AimPoint : perception.LastKnownPosition);
                return;
            }

            // Lean out of tall cover or stand up over low cover, then fire.
            body.SetCrouched(false);
            body.SetLean(cover.Peek == PeekMode.LeanLeft ? -1f : cover.Peek == PeekMode.LeanRight ? 1f : 0f);
            if (visible && !suppressing) EngageTarget(false);
            else SuppressFire();
        }

        private void ActFlank()
        {
            body.SetLean(0f);
            body.SetCrouched(false);
            MoveTo(route[0], runSpeed);
            if (perception.TargetVisible) EngageTarget(true);
            else
            {
                weapon.CeaseFire();
                if ((route[0] - transform.position).sqrMagnitude < 8f * 8f) body.AimAt(PreAimPoint(perception.LastKnownPosition));
                else body.ClearAim();
            }
        }

        private void ActStack()
        {
            body.SetLean(0f);
            body.SetCrouched(false);
            MoveTo(route[0], walkSpeed);
            weapon.CeaseFire();
            if (Squad != null && Squad.IsFlashPending)
            {
                body.LookAt(transform.position + (transform.position - orderLook)); // eyes away from our own flashbang
                return;
            }

            body.AimAt(orderLook);
            if (perception.TargetVisible) EngageTarget(false);
        }

        private void ActBreach()
        {
            body.SetLean(0f);
            body.SetCrouched(false);
            if (routeIndex < routeCount && Arrived(route[routeIndex])) routeIndex++;
            Vector3 position = transform.position;
            if (routeIndex < routeCount) MoveTo(route[routeIndex], walkSpeed);
            else Stop();

            if (perception.TargetVisible)
            {
                EngageTarget(true); // CQB: shoot on the move
                return;
            }

            weapon.CeaseFire();
            if (breachEntry == null) return;
            if (routeIndex < arcCount) body.AimAt(CQBRoom.SliceAim(breachEntry, position, 4f));
            else if (routeIndex == arcCount) body.AimAt(route[arcCount + 1] + breachEntry.forward * 2f + Vector3.up * 1.3f);
            else body.AimAt(breachEntry.position + breachEntry.forward * 3f + Vector3.up * 1.3f);
        }

        private void EngageTarget(bool moving)
        {
            PerceivableTarget target = perception.Target;
            Vector3 point = AimPoint(target, moving);
            body.AimAt(point);
            float distance = Vector3.Distance(body.Muzzle.position, target.AimPoint);
            if (distance > maxEngageRange) return;
            TryFire(point, Random.Range(burstLength.x, burstLength.y + 1), burstPause, false);
        }

        private void SuppressFire()
        {
            if (Time.time >= nextBurstAt && !weapon.IsFiring) suppressPoint = PickSuppressPoint();
            body.AimAt(suppressPoint);
            bool covering = Time.time < coveringUntil;
            TryFire(suppressPoint, Random.Range(2, covering ? 6 : 4), covering ? burstPause : suppressPause, true);
        }

        private Vector3 AimPoint(PerceivableTarget target, bool moving)
        {
            float now = Time.time;
            float settle = 1f + acquisitionSpread * Mathf.Exp(-(now - trackingSince));
            Vector3 targetVelocity = target.Velocity;
            float spread = aimSpread * settle * (1f + perception.Suppression * suppressedSpread) *
                           (1f + 0.3f * new Vector2(targetVelocity.x, targetVelocity.z).magnitude) * (moving ? 2f : 1f);

            Vector3 muzzle = body.Muzzle.position;
            Vector3 toTarget = target.AimPoint - muzzle;
            float distance = toTarget.magnitude;
            Vector3 forward = distance > 1e-3f ? toTarget / distance : transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 up = Vector3.Cross(forward, right);
            float radius = Mathf.Tan(spread * Mathf.Deg2Rad) * distance;
            float x = (Mathf.PerlinNoise(noiseSeed, now * 1.7f) * 2f - 1f) * radius;
            float y = (Mathf.PerlinNoise(noiseSeed + 31.4f, now * 1.7f) * 2f - 1f) * radius;
            return target.AimPoint + right * x + up * y;
        }

        private void TryFire(Vector3 point, int rounds, Vector2 pause, bool suppressive)
        {
            float now = Time.time;
            if (now < fireAllowedAt || now < nextBurstAt || weapon.IsReloading || weapon.IsFiring) return;
            if (body.AimError(point) > aimTolerance) return;

            Transform muzzle = body.Muzzle;
            Vector3 toPoint = point - muzzle.position;
            float distance = toPoint.magnitude;
            if (distance < 1e-3f) return;

            // Never into our own cover; aimed fire also needs a clear line (suppressive fire may chew the target's cover).
            if (Physics.Raycast(muzzle.position, toPoint / distance, out RaycastHit hit, distance, GameLayers.EnvironmentMask, QueryTriggerInteraction.Ignore) &&
                (hit.distance < 1.5f || !suppressive))
                return;
            if (FriendlyInLine(muzzle.position, point)) return;

            weapon.Fire(rounds);
            nextBurstAt = now + rounds * (weapon.Data != null ? weapon.Data.CycleTime : 0.1f) + Random.Range(pause.x, pause.y);
        }

        /// <summary>Anyone on our team (soldier or player) stands within 0.7 m of the line of fire.</summary>
        private bool FriendlyInLine(Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            var targets = PerceivableTarget.All;
            for (int i = 0; i < targets.Count; i++)
            {
                PerceivableTarget friend = targets[i];
                if (friend.Team != perception.Team || friend.transform == transform || !friend.IsAlive) continue;
                Vector3 chest = friend.Chest.position;
                float t = lengthSquared > 1e-6f ? Mathf.Clamp01(Vector3.Dot(chest - from, segment) / lengthSquared) : 0f;
                if ((from + segment * t - chest).sqrMagnitude < 0.7f * 0.7f) return true;
            }

            return false;
        }

        // ---- Movement helpers ----

        private void MoveTo(Vector3 point, float speed)
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = false;
            agent.speed = body.IsCrouched ? Mathf.Min(speed, crouchSpeed) : speed;
            if (!agent.hasPath || (agent.destination - point).sqrMagnitude > 0.09f) agent.SetDestination(point);
        }

        /// <summary>The closest walkable point (arc points hug walls closer than the agent can).</summary>
        private static Vector3 OnNavMesh(Vector3 point) =>
            NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas) ? hit.position : point;

        private void Stop()
        {
            if (!agent.enabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
        }

        private bool Arrived(Vector3 point) => Arrived(point, arriveDistance);

        private bool Arrived(Vector3 point, float distance)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= distance * distance;
        }

        private void SetState(AIState state)
        {
            if (State == state) return;
            if (state == AIState.Investigate && (State == AIState.Engage || State == AIState.Suppress)) Callout(CalloutType.LostContact);
            if (state != AIState.Engage && state != AIState.Suppress) peeking = false;
            State = state;
        }

        // ---- Events ----

        private void OnTargetIdentified(PerceivableTarget target)
        {
            fireAllowedAt = Time.time + Random.Range(reactionTime.x, reactionTime.y);
            trackingSince = Time.time;
            Callout(CalloutType.Contact);
        }

        private void OnReloadStarted()
        {
            Callout(CalloutType.Reloading);
            Squad?.OnMemberReloading(this);
        }

        private void OnDamage(in DamageEvent evt)
        {
            if (evt.Target == health && evt.Direction.sqrMagnitude > 1e-4f) lastHitDirection = evt.Direction;
        }

        private void OnDied()
        {
            State = AIState.Dead;
            weapon.CeaseFire();
            perception.enabled = false;
            body.Die(lastHitDirection);
            Squad?.OnMemberDied(this);
        }
    }
}
