using System.Collections.Generic;
using System.Text;
using FPS.Core;
using FPS.Locomotion;
using FPS.Weapons;
using UnityEngine;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>
    /// The armory workbench. Interact to step up: the weapon in hand is laid out on a turntable under the bench camera
    /// with a marker on every attachment mount. Drag to turn it, click a marker to list the parts that fit that slot,
    /// click a part to fit it — the model, its measured balance and the stats update at once. "Fit to weapon" puts the
    /// build on the carried weapon and saves it (blueprint + loadout); "Leave" (or Menu) hands control back, and the
    /// indoor range next door is a few steps away — no loading screen in between.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GunsmithWorkbenchController : MonoBehaviour, IInteractable
    {
        private const int MaxOptions = 12;
        private static readonly RaycastHit[] PickHits = new RaycastHit[16];
        private static readonly AttachmentSlot[] Slots =
        {
            AttachmentSlot.Barrel, AttachmentSlot.Muzzle, AttachmentSlot.Optic, AttachmentSlot.Underbarrel,
            AttachmentSlot.Stock, AttachmentSlot.Laser, AttachmentSlot.Light,
        };

        [SerializeField] private ArmoryCatalogSO catalog;
        [SerializeField] private Camera benchCamera;
        [SerializeField] private Transform turntable;
        [SerializeField] private string prompt = "Use the gunsmith workbench";
        [Tooltip("Turntable degrees per pixel dragged.")]
        [SerializeField] private float turnRate = 0.35f;
        [SerializeField] private float markerSize = 0.035f;
        [SerializeField] private Material markerMaterial;
        [SerializeField] private Material selectedMarkerMaterial;

        private readonly List<AttachmentDataSO> build = new List<AttachmentDataSO>(8);
        private readonly List<AttachmentDataSO> options = new List<AttachmentDataSO>(MaxOptions);
        private readonly List<WorkbenchNode> nodes = new List<WorkbenchNode>(8);
        private readonly List<Behaviour> paused = new List<Behaviour>(4);
        private readonly Button[] optionButtons = new Button[MaxOptions + 1];
        private readonly StringBuilder text = new StringBuilder(512);
        private PlayerRig user;
        private RealisticWeaponController weapons;
        private TacticalEquipmentSystem equipment;
        private SaveProfileManager profiles;
        private WeaponModelRig model;
        private WeaponDataSO baseData;
        private int slot;
        private float yaw, pitch;
        private GameObject ui;
        private Text title, stats, optionsTitle, status;

        /// <summary>The workbench in use, or null.</summary>
        public static GunsmithWorkbenchController Active { get; private set; }

        public string Prompt => prompt;
        public bool IsActive => Active == this;
        public WeaponModelRig Preview { get; private set; }
        public WeaponDataSO PreviewData { get; private set; }
        public IReadOnlyList<AttachmentDataSO> Build => build;
        public IReadOnlyList<AttachmentDataSO> Options => options;
        public IReadOnlyList<WorkbenchNode> Nodes => nodes;
        public bool HasSelection { get; private set; }
        public AttachmentSlot SelectedSlot { get; private set; }
        public Camera BenchCamera => benchCamera;

        public void Configure(ArmoryCatalogSO armory, Camera view, Transform display, Material marker, Material selected)
        {
            catalog = armory;
            benchCamera = view;
            turntable = display;
            markerMaterial = marker;
            selectedMarkerMaterial = selected;
        }

        public bool CanInteract(GameObject candidate) =>
            Active == null && candidate.TryGetComponent(out RealisticWeaponController carried) && carried.ActiveSlot >= 0 &&
            carried.CurrentAction == WeaponAction.None;

        public void Interact(GameObject candidate)
        {
            if (CanInteract(candidate) && candidate.TryGetComponent(out PlayerRig rig)) Enter(rig);
        }

        /// <summary>Steps <paramref name="rig"/> up to the bench with the weapon in hand.</summary>
        public void Enter(PlayerRig rig)
        {
            Active = this;
            user = rig;
            weapons = rig.GetComponent<RealisticWeaponController>();
            equipment = rig.GetComponent<TacticalEquipmentSystem>();
            profiles = rig.GetComponent<SaveProfileManager>();
            slot = weapons.ActiveSlot;
            weapons.GetLoadoutWeapon(slot, out model, out baseData);
            build.Clear();
            IReadOnlyList<AttachmentDataSO> fitted = weapons.GetAttachments(slot);
            for (int i = 0; i < fitted.Count; i++) build.Add(fitted[i]);

            PlayerControl.Pause(rig.gameObject, paused);
            rig.PlayerCamera.enabled = false;
            benchCamera.enabled = true;
            yaw = 90f;
            pitch = 0f;
            Turn(Vector2.zero);
            HasSelection = false;
            ui.SetActive(true);
            status.text = string.Empty;
            ShowOptions();
            Rebuild();
        }

        public void Leave()
        {
            if (!IsActive) return;
            ClearPreview();
            benchCamera.enabled = false;
            ui.SetActive(false);
            if (user != null)
            {
                user.PlayerCamera.enabled = true;
                PlayerControl.Resume(paused);
            }

            user = null;
            Active = null;
        }

        /// <summary>Turns the weapon on the table by a pointer drag (pixels).</summary>
        public void Rotate(Vector2 delta) => Turn(delta);

        /// <summary>Selects the mount marker under a screen point. False when none is there.</summary>
        public bool Pick(Vector2 screenPosition)
        {
            if (!IsActive) return false;
            Physics.SyncTransforms(); // markers turn with the table between physics steps
            Ray ray = benchCamera.ScreenPointToRay(screenPosition);
            int count = Physics.RaycastNonAlloc(ray, PickHits, 5f, ~0, QueryTriggerInteraction.Collide);
            WorkbenchNode picked = null;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (PickHits[i].distance >= nearest || !PickHits[i].collider.TryGetComponent(out WorkbenchNode node)) continue;
                nearest = PickHits[i].distance;
                picked = node;
            }

            if (picked == null) return false;
            SelectSlot(picked.Slot);
            return true;
        }

        /// <summary>Lists the parts that fit <paramref name="attachmentSlot"/>.</summary>
        public void SelectSlot(AttachmentSlot attachmentSlot)
        {
            HasSelection = true;
            SelectedSlot = attachmentSlot;
            catalog.CompatibleParts(attachmentSlot, model, options);
            if (options.Count > MaxOptions) options.RemoveRange(MaxOptions, options.Count - MaxOptions);
            ShowOptions();
            HighlightNodes();
        }

        /// <summary>Fits <paramref name="part"/> in the selected slot (null takes the slot's part off) and rebuilds.</summary>
        public void Fit(AttachmentDataSO part)
        {
            if (!HasSelection) return;
            for (int i = build.Count - 1; i >= 0; i--)
            {
                if (build[i] == null || build[i].Slot == SelectedSlot) build.RemoveAt(i);
            }

            if (part != null) build.Add(part);
            status.text = string.Empty;
            Rebuild();
            HighlightNodes();
        }

        /// <summary>Puts the build on the carried weapon and saves it as its blueprint and the loadout.</summary>
        public void Apply()
        {
            if (!IsActive) return;
            weapons.SetBuild(slot, build);
            bool saved = profiles != null && profiles.StoreLoadout(weapons, equipment);
            status.text = saved ? "Fitted to your weapon and saved." : "Fitted to your weapon.";
        }

        private void Awake()
        {
            if (benchCamera != null) benchCamera.enabled = false;
            UIFactory.EnsureEventSystem();
            BuildUI();
        }

        private void OnDisable() => Leave();

        private void Turn(Vector2 delta)
        {
            yaw -= delta.x * turnRate;
            pitch = Mathf.Clamp(pitch + delta.y * turnRate, -35f, 35f);
            turntable.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void Rebuild()
        {
            ClearPreview();
            PreviewData = GunsmithAssembler.Assemble(baseData, build);
            Preview = Instantiate(model, turntable);
            Preview.name = $"{model.name} (workbench)";
            Transform previewTransform = Preview.transform;
            previewTransform.localRotation = Quaternion.identity;
            GunsmithAssembler.AttachParts(Preview, PreviewData, build);
            SetLayer(previewTransform, 0);

            // Centre the weapon on the turntable so it turns about its middle.
            Vector3 stock = Preview.StockPivotSocket != null ? previewTransform.InverseTransformPoint(Preview.StockPivotSocket.position) : Vector3.zero;
            Vector3 muzzle = Preview.MuzzleSocket != null ? previewTransform.InverseTransformPoint(Preview.MuzzleSocket.position) : Vector3.zero;
            previewTransform.localPosition = -(stock + muzzle) * 0.5f;

            for (int i = 0; i < Slots.Length; i++)
            {
                Transform mount = Preview.FindNode(AttachmentDataSO.MountName(Slots[i]));
                if (mount != null) nodes.Add(Marker(Slots[i], mount));
            }

            HighlightNodes();
            WriteStats();
        }

        private WorkbenchNode Marker(AttachmentSlot attachmentSlot, Transform mount)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"{attachmentSlot} Node";
            marker.transform.SetParent(turntable, false);
            marker.transform.position = mount.position;
            marker.transform.localScale = Vector3.one * markerSize;
            marker.GetComponent<Collider>().isTrigger = true;
            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = markerMaterial;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            WorkbenchNode node = marker.AddComponent<WorkbenchNode>();
            node.Configure(attachmentSlot, markerRenderer);
            return node;
        }

        private void HighlightNodes()
        {
            for (int i = 0; i < nodes.Count; i++)
                nodes[i].Marker.sharedMaterial = HasSelection && nodes[i].Slot == SelectedSlot ? selectedMarkerMaterial : markerMaterial;
        }

        private void ClearPreview()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null) Destroy(nodes[i].gameObject);
            }

            nodes.Clear();
            if (Preview != null) Destroy(Preview.gameObject);
            if (PreviewData != null) Destroy(PreviewData);
            Preview = null;
            PreviewData = null;
        }

        private void WriteStats()
        {
            WeaponDataSO data = PreviewData;
            float velocity = data.Caliber != null ? data.Caliber.MuzzleVelocity(data.BarrelLengthInches) * data.MuzzleVelocityMultiplier : 0f;
            title.text = data.DisplayName;
            text.Clear();
            text.Append("Mass ").Append(data.MassKg.ToString("F2")).Append(" kg     Ergonomics ").Append(data.Ergonomics.ToString("F0")).Append('\n');
            text.Append("Barrel ").Append(data.BarrelLengthInches.ToString("F1")).Append("\"     Muzzle velocity ").Append(velocity.ToString("F0")).Append(" m/s\n");
            text.Append("Recoil  vertical ×").Append(data.RecoilVerticalMultiplier.ToString("F2"))
                .Append("  horizontal ×").Append(data.RecoilHorizontalMultiplier.ToString("F2"))
                .Append("  rearward ×").Append(data.RecoilBackMultiplier.ToString("F2")).Append('\n');
            text.Append("Aim down sights ").Append(data.AdsTime.ToString("F2")).Append(" s     Swing inertia ").Append(data.MomentOfInertia.ToString("F2")).Append(" kg·m²\n");
            text.Append(data.MuzzleFlashMultiplier < 0.05f ? "Suppressed" : "Muzzle flash ×" + data.MuzzleFlashMultiplier.ToString("F2")).Append("\n\n");
            for (int i = 0; i < build.Count; i++) text.Append(build[i].Slot).Append(": ").Append(build[i].DisplayName).Append('\n');
            stats.text = text.ToString();
        }

        private void ShowOptions()
        {
            optionsTitle.text = HasSelection ? $"{SelectedSlot} — choose a part" : "Click a marker on the weapon";
            int shown = HasSelection ? options.Count + 1 : 0;
            for (int i = 0; i < optionButtons.Length; i++)
            {
                bool visible = i < shown;
                optionButtons[i].gameObject.SetActive(visible);
                if (visible) UIFactory.SetText(optionButtons[i], i == 0 ? "(none)" : options[i - 1].DisplayName);
            }
        }

        private void BuildUI()
        {
            RectTransform root = UIFactory.Canvas(transform, "Workbench UI", 20, true);
            ui = root.gameObject;

            RectTransform surface = UIFactory.Panel(root, "Turntable Control", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f), new Color(0f, 0f, 0f, 0f));
            surface.GetComponent<Image>().raycastTarget = true;
            surface.gameObject.AddComponent<WorkbenchInputSurface>().Bind(this);

            RectTransform left = UIFactory.Panel(root, "Stats", new Vector2(0f, 0.5f), new Vector2(330f, 120f), new Vector2(600f, 520f), UIFactory.PanelColor);
            title = UIFactory.Label(left, 28, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(560f, 44f), TextAnchor.MiddleLeft);
            stats = UIFactory.Label(left, 19, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(560f, 420f), TextAnchor.UpperLeft);

            RectTransform right = UIFactory.Panel(root, "Parts", new Vector2(1f, 0.5f), new Vector2(-260f, 60f), new Vector2(440f, 760f), UIFactory.PanelColor);
            optionsTitle = UIFactory.Label(right, 22, new Vector2(0.5f, 1f), new Vector2(0f, -32f), new Vector2(400f, 40f), TextAnchor.MiddleCenter);
            for (int i = 0; i < optionButtons.Length; i++)
            {
                int index = i;
                optionButtons[i] = UIFactory.Button(right, "Part", new Vector2(0.5f, 1f), new Vector2(0f, -82f - i * 50f), new Vector2(400f, 42f),
                    () => Fit(index == 0 ? null : options[index - 1]));
            }

            UIFactory.Button(root, "Fit to weapon", new Vector2(0.5f, 0f), new Vector2(-140f, 70f), new Vector2(250f, 50f), Apply);
            UIFactory.Button(root, "Leave", new Vector2(0.5f, 0f), new Vector2(140f, 70f), new Vector2(250f, 50f), Leave);
            status = UIFactory.Label(root, 22, new Vector2(0.5f, 0f), new Vector2(0f, 130f), new Vector2(800f, 36f), TextAnchor.MiddleCenter);
            ui.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetActive() => Active = null;

        private static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
        }
    }
}
