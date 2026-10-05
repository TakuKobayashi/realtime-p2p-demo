using System;
using System.Linq;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace net.taptappun.RealtimeP2PKit.Localization
{
    public static class LocalizedUGUIText
    {
        // Table references and UnityEvent bindings belong to the scene/prefab, not the library.
        public static void SetEntry(Text target, TableEntryReference entry, params object[] arguments)
        {
            var label = GetLabel(target);
            var reference = label.StringReference;
            if (reference.TableEntryReference.Equals(entry) &&
                (reference.Arguments ?? Array.Empty<object>()).SequenceEqual(arguments)) return;
            reference.Arguments = arguments;
            reference.TableEntryReference = entry;
            label.enabled = true;
            label.RefreshString();
        }

        public static void SetMessage(Text target, LocalizedString message)
        {
            var label = GetLabel(target);
            if (message == null)
            {
                label.enabled = false;
                target.text = string.Empty;
                return;
            }
            if (!ReferenceEquals(label.StringReference, message)) label.StringReference = message;
            label.enabled = true;
        }

        private static LocalizeStringEvent GetLabel(Text target)
        {
            var label = target.GetComponent<LocalizeStringEvent>();
            if (label == null) throw new InvalidOperationException($"{target.name} requires a LocalizeStringEvent component.");
            return label;
        }
    }
}
