using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PhantomCatWorks.RealtimeP2PKit.Example.Editor
{
    /// <summary>Creates editable UGUI hierarchies in assets, never in a runtime bootstrap.</summary>
    public static class ExampleUiBuilder
    {
        private const string RowPath = "Assets/Example/Prefabs/RoomListRow.prefab";
        private static readonly Color PanelColor = new(0.08f, 0.12f, 0.19f, 0.96f);
        private static readonly Color RowColor = new(0.14f, 0.20f, 0.29f, 1f);
        private static readonly Color AccentColor = new(0.12f, 0.47f, 0.72f, 1f);

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static void AddMatchingUi(MatchingRoomExampleController controller)
        {
            if (controller == null) throw new InvalidOperationException("Matching scene controller is missing.");
            var existing = controller.GetComponentInChildren<ExampleMatchingRoomView>(true);
            if (existing != null) { Assign(controller, "_view", existing); return; }
            var canvas = CreateCanvas("MatchingRoomCanvas", controller.transform);
            var view = canvas.gameObject.AddComponent<ExampleMatchingRoomView>();
            var panel = CreatePanel("RoomBrowser", canvas, PanelColor);
            Stretch(panel, new Vector2(0.08f, 0.06f), new Vector2(0.92f, 0.94f));
            TopLeft(CreateText("Title", panel, "Room Matching", 30).rectTransform, 24, 20, 600, 42);
            TopLeft(CreateText("CapacityLabel", panel, "定員（自分を含む人数 / 0 = 無制限）", 18).rectTransform, 24, 72, 650, 28);

            var inputRect = CreatePanel("CapacityInput", panel, Color.white);
            TopLeft(inputRect, 24, 108, 128, 44);
            var input = inputRect.gameObject.AddComponent<InputField>();
            input.targetGraphic = inputRect.GetComponent<Image>();
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 10;
            var inputText = CreateText("Text", inputRect, "4", 22);
            inputText.color = Color.black;
            Stretch(inputText.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 4), new Vector2(-12, -4));
            input.textComponent = inputText;
            input.text = "4";
            var create = CreateButton("CreateRoom", panel, "新しいRoomを作成");
            TopLeft(create.GetComponent<RectTransform>(), 172, 108, 240, 44);
            var refresh = CreateButton("RefreshRooms", panel, "一覧を更新");
            TopRight(refresh.GetComponent<RectTransform>(), 24, 108, 160, 44);
            var status = CreateText("Status", panel, "", 17);
            Stretch(status.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(24, -216), new Vector2(-24, -166));

            var list = CreatePanel("RoomList", panel, new Color(0.04f, 0.07f, 0.12f, 1));
            Stretch(list, Vector2.zero, Vector2.one, new Vector2(24, 24), new Vector2(-24, -232));
            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var viewport = CreateRect("Viewport", list);
            Stretch(viewport, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(-24, 0));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            var empty = CreateText("EmptyRooms", viewport, "Roomはありません。新しいRoomを作成できます。", 20);
            empty.alignment = TextAnchor.MiddleCenter;
            Stretch(empty.rectTransform, Vector2.zero, Vector2.one, new Vector2(20, 20), new Vector2(-20, -20));

            var bar = CreatePanel("VerticalScrollbar", list, RowColor);
            Stretch(bar, new Vector2(1, 0), Vector2.one, new Vector2(-20, 4), new Vector2(-4, -4));
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            var handle = CreatePanel("Handle", bar, AccentColor);
            Stretch(handle, Vector2.zero, Vector2.one);
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;

            Assign(view, "_capacityInput", input, "_createButton", create, "_refreshButton", refresh,
                "_statusText", status, "_emptyText", empty, "_roomContent", content, "_roomRowPrefab", EnsureRowPrefab());
            Assign(controller, "_view", view);
        }

        public static void AddGameplayUi(ExampleBootstrap bootstrap)
        {
            if (bootstrap == null) throw new InvalidOperationException("Gameplay bootstrap is missing.");
            var existing = bootstrap.GetComponentInChildren<ExampleGameplayView>(true);
            if (existing != null) { Assign(bootstrap, "_view", existing); return; }
            var canvas = CreateCanvas("GameplayCanvas", bootstrap.transform);
            var view = canvas.gameObject.AddComponent<ExampleGameplayView>();
            var panel = CreatePanel("RoomHUD", canvas, PanelColor);
            TopLeft(panel, 24, 24, 460, 240);
            var room = CreateText("RoomSummary", panel, "Room", 24);
            TopLeft(room.rectTransform, 20, 16, 420, 36);
            var player = CreateText("PlayerSummary", panel, "Player", 18);
            TopLeft(player.rectTransform, 20, 60, 420, 28);
            var status = CreateText("Status", panel, "Roomに接続中...", 17);
            TopLeft(status.rectTransform, 20, 98, 420, 64);
            var leave = CreateButton("LeaveRoom", panel, "Roomから退出");
            TopLeft(leave.GetComponent<RectTransform>(), 20, 178, 420, 42);
            Assign(view, "_roomText", room, "_playerText", player, "_statusText", status, "_leaveButton", leave);
            Assign(bootstrap, "_view", view);
        }

        private static ExampleRoomRow EnsureRowPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ExampleRoomRow>(RowPath);
            if (existing != null) return existing;
            var root = CreatePanel("RoomListRow", null, RowColor);
            root.sizeDelta = new Vector2(800, 72);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;
            var row = root.gameObject.AddComponent<ExampleRoomRow>();
            var title = CreateText("RoomName", root, "Room", 22);
            Stretch(title.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -36), new Vector2(-140, -8));
            var capacity = CreateText("Capacity", root, "参加者 0 / 4", 16);
            Stretch(capacity.rectTransform, new Vector2(0, 1), Vector2.one, new Vector2(16, -64), new Vector2(-140, -38));
            var join = CreateButton("JoinRoom", root, "Join");
            TopRight(join.GetComponent<RectTransform>(), 12, 14, 112, 44);
            Assign(row, "_roomText", title, "_capacityText", capacity, "_joinButton", join, "_joinLabel", join.GetComponentInChildren<Text>());
            var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, RowPath);
            Object.DestroyImmediate(root.gameObject);
            return prefab.GetComponent<ExampleRoomRow>();
        }

        private static RectTransform CreateCanvas(string name, Transform parent)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            rect.gameObject.AddComponent<GraphicRaycaster>();
            return rect;
        }
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            return rect;
        }
        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }
        private static Text CreateText(string name, Transform parent, string value, int size)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.text = value;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }
        private static Button CreateButton(string name, Transform parent, string label)
        {
            var rect = CreatePanel(name, parent, AccentColor);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var text = CreateText("Label", rect, label, 19);
            text.alignment = TextAnchor.MiddleCenter;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(8, 4), new Vector2(-8, -4));
            return button;
        }
        private static void TopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void TopRight(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
        private static void Assign(Object target, params object[] fields)
        {
            var serialized = new SerializedObject(target);
            for (var i = 0; i < fields.Length; i += 2)
                serialized.FindProperty((string)fields[i]).objectReferenceValue = (Object)fields[i + 1];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
