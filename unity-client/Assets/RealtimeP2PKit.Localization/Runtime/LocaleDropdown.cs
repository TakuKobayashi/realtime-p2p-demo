using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace net.taptappun.RealtimeP2PKit.Localization
{
    [RequireComponent(typeof(Dropdown))]
    public sealed class LocaleDropdown : MonoBehaviour
    {
        private Dropdown _dropdown;
        private List<Locale> _locales;

        private void OnEnable()
        {
            _dropdown = GetComponent<Dropdown>();
            _dropdown.interactable = false;
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            // Async initialization also works in WebGL, where synchronous Addressables loading is unsupported.
            yield return LocalizationSettings.InitializationOperation;
            _locales = LocalizationSettings.AvailableLocales.Locales.ToList();
            _dropdown.ClearOptions();
            _dropdown.AddOptions(_locales.Select(locale => locale.LocaleName).ToList());
            OnLocaleChanged(LocalizationSettings.SelectedLocale);
            _dropdown.onValueChanged.AddListener(SelectLocale);
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
            _dropdown.interactable = _locales.Count > 1;
        }

        private void SelectLocale(int index)
        {
            if (index >= 0 && index < _locales.Count) LocalizationSettings.SelectedLocale = _locales[index];
        }

        private void OnLocaleChanged(Locale locale)
        {
            var index = _locales.IndexOf(locale);
            if (index >= 0) _dropdown.SetValueWithoutNotify(index);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(SelectLocale);
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }
    }
}
