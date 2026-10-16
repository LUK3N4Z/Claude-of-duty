using System;
using System.Collections.Generic;
using FPS.Core;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.AI
{
    /// <summary>
    /// Shared mind of a fire team (the brains under this object). Pools what each soldier knows so one sighting alerts
    /// everyone; when the target goes to ground it sends a flanker round one side while the rest keep it suppressed —
    /// and calls "Target suppressed!" when squad fire is cracking past the target's head. A target holed up in a
    /// <see cref="CQBRoom"/> gets a room clearance: stack on the nearest door, flashbang, then the team slices the pie
    /// through the doorway one by one and takes the points of domination. Also arbitrates grenades, callouts, cover
    /// claims and friendly fire.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquadCoordinator : MonoBehaviour
    {
        private enum RoomPhase
        {
            None,
            Stacking,
            Flashing,
            Entering,
        }

        private const int MaxStack = 4;
        private const int ArcPoints = 4;
        private static readonly int CalloutCount = Enum.GetValues(typeof(CalloutType)).Length;

        [SerializeField, Range(0.1f, 2f)] private float thinkInterval = 0.4f;
        [Tooltip("Time without eyes on the target before a flank or room clearance starts (s).")]
        [SerializeField, Min(0f)] private float flankDelay = 2.5f;
        [Tooltip("How far round the last known position the flanker goes (m).")]
        [SerializeField, Min(2f)] private float flankDistance = 9f;
        [SerializeField, Range(20f, 120f)] private float flankAngle = 75f;
        [Tooltip("Squad fire within this window before a near miss on the target counts as suppressing it (s).")]
        [SerializeField, Min(0f)] private float suppressedWindow = 0.35f;
        [SerializeField, Min(0f)] private float suppressedCalloutCooldown = 6f;
        [SerializeField, Min(0f)] private float grenadeCooldown = 10f;
        [Tooltip("Squad-wide repeat guard for shared callouts (contact, clear, lost contact...) (s).")]
        [SerializeField, Min(0f)] private float sharedCalloutCooldown = 3f;

        [Header("Room Clearing")]
        [SerializeField, Min(0.5f)] private float stackSpacing = 0.9f;
        [Tooltip("Sideways offset of the stack from the doorway centre, against the wall (m).")]
        [SerializeField, Min(0.3f)] private float stackOffset = 1.05f;
        [SerializeField, Min(1f)] private float stackTimeout = 8f;
        [SerializeField, Min(0.5f)] private float flashWait = 3f;
        [SerializeField, Min(0f)] private float entryInterval = 0.6f;
        [SerializeField, Min(1f)] private float clearTimeout = 12f;
        [SerializeField, Min(0.5f)] private float sliceRadius = 1.4f;
        [SerializeField, Range(10f, 89f)] private float sliceStartAngle = 70f;

        private readonly List<TacticalAIBrain> members = new List<TacticalAIBrain>(8);
        private readonly List<Vector3> claimedCover = new List<Vector3>(8);
        private readonly TacticalAIBrain[] stack = new TacticalAIBrain[MaxStack];
        private readonly bool[] entered = new bool[MaxStack];
        private readonly Vector3[] arc = new Vector3[ArcPoints];
        private float[] lastCallout;
        private float nextThink, lastSeenBySquad = float.NegativeInfinity, nextGrenade, lastSuppressedCallout = float.NegativeInfinity;
        private PerceivableTarget target;
        private Vector3 lastKnownPosition;
        private float infoTime = float.NegativeInfinity;
        private RoomPhase phase;
        private CQBRoom room, clearedRoom;
        private Transform entry;
        private float phaseStart, clearedAt = float.NegativeInfinity, stackSide;
        private int stackCount;
        private TacticalAIBrain flashThrower;

        public IReadOnlyList<TacticalAIBrain> Members => members;
        public TacticalAIBrain Flanker { get; private set; }
        public bool IsClearingRoom => phase != RoomPhase.None;
        public CQBRoom ClearingRoom => phase != RoomPhase.None ? room : null;
        /// <summary>A squad flashbang is in the air: the stack looks away.</summary>
        public bool IsFlashPending => phase == RoomPhase.Flashing;
        public PerceivableTarget Target => target;
        public Vector3 LastKnownPosition => lastKnownPosition;

        public void Register(TacticalAIBrain brain)
        {
            if (brain == null || members.Contains(brain)) return;
            members.Add(brain);
            claimedCover.Add(new Vector3(float.NaN, 0f, 0f));
            brain.JoinSquad(this);
        }

        // ---- Services for members ----

        /// <summary>False when a teammate shouted the same shared callout moments ago.</summary>
        public bool TryClaimCallout(CalloutType type)
        {
            bool shared = type == CalloutType.Contact || type == CalloutType.LostContact || type == CalloutType.Clear ||
                          type == CalloutType.StackUp || type == CalloutType.Breaching || type == CalloutType.ManDown;
            if (!shared) return true;

            float now = Time.time;
            if (now - lastCallout[(int)type] < sharedCalloutCooldown) return false;
            lastCallout[(int)type] = now;
            return true;
        }

        public bool RequestGrenade(TacticalAIBrain brain)
        {
            if (phase != RoomPhase.None || Time.time < nextGrenade) return false;
            nextGrenade = Time.time + grenadeCooldown;
            return true;
        }

        public void ClaimCover(TacticalAIBrain brain, Vector3 point)
        {
            int index = members.IndexOf(brain);
            if (index >= 0) claimedCover[index] = point;
        }

        /// <summary>Cover points held by the rest of the squad, written to <paramref name="buffer"/>.</summary>
        public int FillCoverClaims(TacticalAIBrain brain, Vector3[] buffer)
        {
            int count = 0;
            for (int i = 0; i < members.Count && count < buffer.Length; i++)
            {
                if (members[i] == brain || members[i].IsDead || float.IsNaN(claimedCover[i].x)) continue;
                buffer[count++] = claimedCover[i];
            }

            return count;
        }

        /// <summary>A teammate stands within 0.7 m of the line of fire from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public bool IsTeammateInLine(TacticalAIBrain shooter, Vector3 from, Vector3 to)
        {
            Vector3 segment = to - from;
            float lengthSquared = segment.sqrMagnitude;
            for (int i = 0; i < members.Count; i++)
            {
                TacticalAIBrain mate = members[i];
                if (mate == shooter || mate.IsDead) continue;
                Vector3 body = mate.Body.Chest.position;
                float t = lengthSquared > 1e-6f ? Mathf.Clamp01(Vector3.Dot(body - from, segment) / lengthSquared) : 0f;
                Vector3 closest = from + segment * t;
                if ((closest - body).sqrMagnitude < 0.7f * 0.7f) return true;
            }

            return false;
        }

        public void OnFlankFinished(TacticalAIBrain brain)
        {
            if (Flanker == brain) Flanker = null;
        }

        public void OnMemberReloading(TacticalAIBrain brain)
        {
            TacticalAIBrain cover = Nearest(brain.transform.position, brain, true);
            if (cover == null) return;
            cover.Callout(CalloutType.Covering);
            cover.ProvideCoveringFire(3f);
        }

        public void OnMemberDied(TacticalAIBrain brain)
        {
            int index = members.IndexOf(brain);
            if (index >= 0) claimedCover[index] = new Vector3(float.NaN, 0f, 0f);
            if (Flanker == brain) Flanker = null;
            TacticalAIBrain witness = Nearest(brain.transform.position, brain, false);
            if (witness != null) witness.Callout(CalloutType.ManDown);
        }

        // ---- Thinking ----

        private void Awake()
        {
            lastCallout = new float[CalloutCount];
            for (int i = 0; i < CalloutCount; i++) lastCallout[i] = float.NegativeInfinity;
            foreach (TacticalAIBrain brain in GetComponentsInChildren<TacticalAIBrain>(true)) Register(brain);
        }

        private void OnEnable() => EventBus<SuppressionEvent>.Subscribe(OnSuppression);

        private void OnDisable() => EventBus<SuppressionEvent>.Unsubscribe(OnSuppression);

        private void Update()
        {
            if (Time.time < nextThink) return;
            nextThink = Time.time + thinkInterval;
            Think();
        }

        private void Think()
        {
            bool anyVisible = PoolKnowledge(out bool anyIdentified);
            if (phase != RoomPhase.None)
            {
                UpdateRoomClearance(anyVisible);
                return;
            }

            if (!anyIdentified || target == null) return;
            if (anyVisible || Time.time - lastSeenBySquad < flankDelay) return;

            CQBRoom hideout = CQBRoom.At(lastKnownPosition);
            if (hideout != null && hideout.EntryCount > 0 && (hideout != clearedRoom || infoTime > clearedAt))
            {
                BeginRoomClearance(hideout);
                return;
            }

            if (Flanker == null) AssignFlank();
        }

        /// <summary>Spreads the freshest knowledge to every member. Returns whether anyone sees the target.</summary>
        private bool PoolKnowledge(out bool anyIdentified)
        {
            anyIdentified = false;
            bool anyVisible = false;
            TacticalAIBrain source = null;
            float freshest = float.NegativeInfinity;
            for (int i = 0; i < members.Count; i++)
            {
                TacticalAIBrain brain = members[i];
                if (brain.IsDead) continue;
                AIPerceptionSensor perception = brain.Perception;
                if (perception.Target == null) continue;

                anyIdentified |= perception.TargetIdentifiedNow;
                if (perception.TargetVisible)
                {
                    anyVisible = true;
                    lastSeenBySquad = Time.time;
                }

                float info = Time.time - perception.TimeSinceInfo;
                if (info <= freshest) continue;
                freshest = info;
                source = brain;
            }

            if (source == null) return false;
            AIPerceptionSensor best = source.Perception;
            target = best.Target;
            lastKnownPosition = best.LastKnownPosition;
            infoTime = freshest;

            for (int i = 0; i < members.Count; i++)
            {
                TacticalAIBrain brain = members[i];
                if (brain == source || brain.IsDead) continue;
                brain.Perception.ShareKnowledge(target, lastKnownPosition, freshest, best.TargetIdentifiedNow);
            }

            return anyVisible;
        }

        private void AssignFlank()
        {
            int alive = AliveCount(out Vector3 centroid);
            if (alive < 2) return;

            Vector3 approach = lastKnownPosition - centroid;
            approach.y = 0f;
            if (approach.sqrMagnitude < 1e-3f) return;
            approach.Normalize();

            for (int attempt = 0; attempt < 2; attempt++)
            {
                float sign = (attempt == 0) == (Time.frameCount % 2 == 0) ? 1f : -1f;
                Vector3 offset = Quaternion.Euler(0f, sign * flankAngle, 0f) * -approach * flankDistance;
                if (!NavMesh.SamplePosition(lastKnownPosition + offset, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;

                TacticalAIBrain flanker = Nearest(hit.position, null, true);
                if (flanker == null) return;

                // Seen from the squad's side, an offset to the left of the approach line is a left flank.
                bool left = Vector3.Cross(approach, hit.position - lastKnownPosition).y < 0f;
                Flanker = flanker;
                flanker.OrderFlank(hit.position, left);
                return;
            }
        }

        private void OnSuppression(in SuppressionEvent evt)
        {
            if (target == null || evt.Listener != target.Listener) return;

            float now = Time.time;
            TacticalAIBrain shooter = null;
            float latest = now - suppressedWindow;
            for (int i = 0; i < members.Count; i++)
            {
                TacticalAIBrain brain = members[i];
                if (brain.IsDead || brain.WeaponHandler.LastShotTime < latest) continue;
                latest = brain.WeaponHandler.LastShotTime;
                shooter = brain;
            }

            if (shooter == null || now - lastSuppressedCallout < suppressedCalloutCooldown) return;
            lastSuppressedCallout = now;
            shooter.Callout(CalloutType.TargetSuppressed);
            if (Flanker == null && phase == RoomPhase.None) AssignFlank(); // suppress, then move
        }

        // ---- Room clearance ----

        private void BeginRoomClearance(CQBRoom hideout)
        {
            AliveCount(out Vector3 centroid);
            Transform door = hideout.NearestEntry(centroid);
            if (door == null) return;

            room = hideout;
            entry = door;
            phase = RoomPhase.Stacking;
            phaseStart = Time.time;
            Flanker = null;
            flashThrower = null;
            Vector3 entryRight = Vector3.Cross(Vector3.up, door.forward);
            stackSide = Vector3.Dot(centroid - door.position, entryRight) >= 0f ? 1f : -1f;

            stackCount = 0;
            for (int i = 0; i < MaxStack; i++)
            {
                stack[i] = null;
                entered[i] = false;
            }

            // Closest soldiers first: the point man leads the stack.
            for (int slot = 0; slot < MaxStack; slot++)
            {
                TacticalAIBrain next = null;
                float best = float.PositiveInfinity;
                for (int i = 0; i < members.Count; i++)
                {
                    TacticalAIBrain brain = members[i];
                    if (brain.IsDead || brain.Perception.IsStunned || InStack(brain)) continue;
                    float distance = (brain.transform.position - door.position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    next = brain;
                }

                if (next == null) break;
                Vector3 point = door.position - door.forward * (0.9f + stackSpacing * slot) + entryRight * (stackSide * stackOffset);
                if (NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) point = hit.position;
                stack[stackCount++] = next;
                next.OrderStack(point, door.position + Vector3.up * 1.3f);
            }

            if (stackCount == 0)
            {
                phase = RoomPhase.None;
                return;
            }

            stack[0].Callout(CalloutType.StackUp);
        }

        private void UpdateRoomClearance(bool anyVisible)
        {
            float now = Time.time;
            if (anyVisible && !room.Contains(lastKnownPosition))
            {
                EndRoomClearance(false); // the target is not in there any more
                return;
            }

            switch (phase)
            {
                case RoomPhase.Stacking:
                    if (!AllStacked() && now - phaseStart < stackTimeout) return;
                    phaseStart = now;
                    flashThrower = null;
                    for (int i = 0; i < stackCount && flashThrower == null; i++)
                    {
                        TacticalAIBrain brain = stack[i];
                        if (brain.IsDead || !brain.HasGrenade(GrenadeType.Flashbang)) continue;
                        Vector3 through = entry.position - brain.transform.position;
                        through.y = 0f;
                        Vector3 landing = entry.position + through.normalized * 2.5f;
                        if (brain.OrderThrow(GrenadeType.Flashbang, landing, false)) flashThrower = brain;
                    }

                    phase = flashThrower != null ? RoomPhase.Flashing : RoomPhase.Entering;
                    if (phase == RoomPhase.Entering) stack[0].Callout(CalloutType.Breaching);
                    return;

                case RoomPhase.Flashing:
                    ThrownGrenade bang = flashThrower != null ? flashThrower.LastGrenade : null;
                    if (bang != null && !bang.HasDetonated && now - phaseStart < flashWait) return;
                    phase = RoomPhase.Entering;
                    phaseStart = now;
                    stack[0].Callout(CalloutType.Breaching);
                    return;

                case RoomPhase.Entering:
                    bool allDone = true;
                    for (int i = 0; i < stackCount; i++)
                    {
                        TacticalAIBrain brain = stack[i];
                        if (brain.IsDead) continue;
                        if (!entered[i])
                        {
                            allDone = false;
                            // One man through the door at a time: wait until the one ahead is inside.
                            if (now < phaseStart + i * entryInterval || (i > 0 && !Through(i - 1)) || brain.Perception.IsStunned) continue;
                            entered[i] = true;
                            int count = CQBRoom.SliceArc(entry, stackSide, sliceRadius, sliceStartAngle, arc, ArcPoints);
                            Vector3 corner = room.DominationPointCount > 0 ? room.GetDominationPoint(i % room.DominationPointCount).position : room.Center;
                            brain.OrderBreach(entry, arc, count, corner);
                            continue;
                        }

                        if (brain.State == AIState.Breach && !brain.IsOrderComplete) allDone = false;
                    }

                    if (allDone || now - phaseStart > clearTimeout) EndRoomClearance(!anyVisible);
                    return;
            }
        }

        private void EndRoomClearance(bool announceClear)
        {
            if (announceClear)
            {
                clearedRoom = room;
                clearedAt = Time.time;
                for (int i = 0; i < stackCount; i++)
                {
                    if (stack[i].IsDead) continue;
                    stack[i].Callout(CalloutType.Clear);
                    break;
                }
            }

            for (int i = 0; i < stackCount; i++) stack[i].ReleaseOrders();
            phase = RoomPhase.None;
            stackCount = 0;
            flashThrower = null;
        }

        private bool Through(int slot)
        {
            TacticalAIBrain ahead = stack[slot];
            return ahead.IsDead || ahead.State != AIState.Breach || ahead.PastDoorway;
        }

        private bool AllStacked()
        {
            for (int i = 0; i < stackCount; i++)
            {
                if (!stack[i].IsDead && stack[i].State == AIState.Stack && !stack[i].IsOrderComplete) return false;
            }

            return true;
        }

        private bool InStack(TacticalAIBrain brain)
        {
            for (int i = 0; i < stackCount; i++)
            {
                if (stack[i] == brain) return true;
            }

            return false;
        }

        private int AliveCount(out Vector3 centroid)
        {
            int alive = 0;
            centroid = Vector3.zero;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].IsDead) continue;
                centroid += members[i].transform.position;
                alive++;
            }

            if (alive > 0) centroid /= alive;
            return alive;
        }

        private TacticalAIBrain Nearest(Vector3 point, TacticalAIBrain exclude, bool ready)
        {
            TacticalAIBrain nearest = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < members.Count; i++)
            {
                TacticalAIBrain brain = members[i];
                if (brain == exclude || brain.IsDead) continue;
                if (ready && (brain.WeaponHandler.IsReloading || brain.Perception.IsStunned)) continue;
                float distance = (brain.transform.position - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = brain;
            }

            return nearest;
        }
    }
}
