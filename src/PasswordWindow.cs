using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public class PasswordWindow : SingleAutoLayoutWindow<PasswordWindow>
    {
        const float Width = 190f;
        LocalizedText _what;
        TextInput _input;
        static bool _wrong;
        public static bool Pending;

        public static void Open(bool wrong)
        {
            _wrong = wrong;
            Pending = true;
            if (Instance == null) CreateWindow("wbmp_password", Lang.Id("password"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_password");
            Instance.Refresh();
        }

        protected override void Init()
        {
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 10, 12);
            Image lockIcon = Ui.Image(transform, Icons.Get("lock"), new Vector2(28f, 28f));
            _what = Ui.Label(transform, null, 9, Ui.Gold, new Vector2(Width, 16f), TextAnchor.MiddleCenter);
            _input = Ui.Input(transform, "", Width - 10f, null);
            _input.input.contentType = InputField.ContentType.Password;
            _input.input.onEndEdit.AddListener(_ => { if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) Submit(); });
            Ui.Button(transform, "join", "join", true, Submit, Width);
        }

        public override void OnNormalEnable() => Refresh();

        void Refresh()
        {
            if (_input == null) return;
            Lang.Live(_what, "password_what", Lang.T(_wrong ? "wrong_password" : "need_password"));
            _input.input.text = "";
            _input.input.Select();
            _input.input.ActivateInputField();
        }

        void Submit() => Enter(_input.input.text);

        public static void Enter(string password)
        {
            Pending = false;
            ScrollWindow.hideAllEvent(false);
            MPMod.Instance.Rejoin(password ?? "");
        }
    }
}
