#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using net.taptappun.RealtimeP2PKit.Localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace net.taptappun.RealtimeP2PKit.Example.Tests
{
    public sealed class LocalizationTests
    {
        [UnityTest]
        public IEnumerator SwitchingLocaleUpdatesExistingTextAndFormatting()
        {
            yield return LocalizationSettings.InitializationOperation;
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            var root = new GameObject("Localized text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.SetActive(false);
            var text = root.GetComponent<Text>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Example/Localization/Fonts/NotoSansJP.ttf");
            var label = root.AddComponent<LocalizeStringEvent>();
            label.StringReference = ExampleLocalization.Message("row.capacity", 2, 4);
            label.OnUpdateString.AddListener(value => text.text = value);
            root.SetActive(true);
            yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.AreEqual("Participants 2 / 4", text.text);
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("ja");
            yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.AreEqual("参加者 2 / 4", text.text);
            Assert.IsTrue(text.font.HasCharacter('日'));
            LocalizedUGUIText.SetEntry(text, "row.capacity_unlimited", 3);
            yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.AreEqual("参加者 3 / 無制限", text.text);
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.AreEqual("Participants 3 / Unlimited", text.text);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator LanguageDropdownUsesConfiguredLocales()
        {
            yield return LocalizationSettings.InitializationOperation;
            var root = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            var dropdown = root.GetComponent<Dropdown>();
            root.AddComponent<LocaleDropdown>();
            yield return null;
            yield return null;
            Assert.AreEqual(LocalizationSettings.AvailableLocales.Locales.Count, dropdown.options.Count);
            var japanese = LocalizationSettings.AvailableLocales.Locales.FindIndex(locale => locale.Identifier.Code == "ja");
            dropdown.value = japanese;
            Assert.AreEqual("ja", LocalizationSettings.SelectedLocale.Identifier.Code);
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            yield return LocalizationSettings.InitializationOperation;
            yield return null;
            Assert.AreEqual("English", dropdown.options[dropdown.value].text);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator PrefabBindingsUpdateTextAndStatusInBothLanguages()
        {
            yield return LocalizationSettings.InitializationOperation;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Example/Prefabs/RoomListRow.prefab");
            var root = Object.Instantiate(prefab);
            var row = root.GetComponent<ExampleRoomRow>();
            row.Bind(new Matchmaking.MachingRoom { id = 42, memberCount = 4, maxPlayers = 4 }, null, false);
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("en");
            foreach (var label in root.GetComponentsInChildren<LocalizeStringEvent>()) yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(text => text.text == "Full"));
            LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.GetLocale("ja");
            foreach (var label in root.GetComponentsInChildren<LocalizeStringEvent>()) yield return label.StringReference.GetLocalizedStringAsync();
            yield return null;
            Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(text => text.text == "満員"));
            Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(text => text.text == "ルーム 42"));
            Object.Destroy(root);
        }
    }
}

#endif
