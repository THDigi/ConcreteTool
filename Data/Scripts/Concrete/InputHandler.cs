using System.Collections.Generic;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using static Sandbox.ModAPI.MyAPIGateway;

namespace Digi
{
    public static class InputHandler
    {
        public static bool IsInputReadable()
        {
            return !Gui.IsCursorVisible && !Gui.ChatEntryVisible;
        }

        /// <summary>
        /// Reads the bound button or key from the given control to bypass control blocking.
        /// </summary>
        public static bool IsControlPressedIgnoreBlock(MyStringId controlId, bool newPress = false)
        {
            IMyControl control = Input.GetGameControl(controlId);

            // WARNING: IsKeyPress(MyKeys.None) returns true for some people!

            if(newPress)
            {
                MyMouseButtonsEnum button = control.GetMouseControl();
                if(button != MyMouseButtonsEnum.None && Input.IsNewMousePressed(button))
                    return true;

                MyKeys kb1 = control.GetKeyboardControl();
                if(kb1 != MyKeys.None && Input.IsNewKeyPressed(kb1))
                    return true;

                MyKeys kb2 = control.GetSecondKeyboardControl();
                if(kb2 != MyKeys.None && Input.IsNewKeyPressed(kb2))
                    return true;
            }
            else
            {
                MyMouseButtonsEnum button = control.GetMouseControl();
                if(button != MyMouseButtonsEnum.None && Input.IsMousePressed(button))
                    return true;

                MyKeys kb1 = control.GetKeyboardControl();
                if(kb1 != MyKeys.None && Input.IsKeyPress(kb1))
                    return true;

                MyKeys kb2 = control.GetSecondKeyboardControl();
                if(kb2 != MyKeys.None && Input.IsKeyPress(kb2))
                    return true;
            }

            return false;
        }

        public static string GetAssignedGameControlNames(MyStringId controlId, bool oneResult = false)
        {
            return GetAssignedGameControlNames(Input.GetGameControl(controlId), oneResult);
        }

        public static string GetAssignedGameControlNames(IMyControl control, bool oneResult = false)
        {
            List<string> inputs = (oneResult ? null : new List<string>());

            if(control.GetMouseControl() != MyMouseButtonsEnum.None)
            {
                string name = Input.GetName(control.GetMouseControl());

                if(oneResult)
                    return name;
                else
                    inputs.Add(name);
            }

            if(control.GetKeyboardControl() != MyKeys.None)
            {
                string name = Input.GetKeyName(control.GetKeyboardControl());

                if(oneResult)
                    return name;
                else
                    inputs.Add(name);
            }

            if(control.GetSecondKeyboardControl() != MyKeys.None)
            {
                string name = Input.GetKeyName(control.GetSecondKeyboardControl());

                if(oneResult)
                    return name;
                else
                    inputs.Add(name);
            }

            return (oneResult || inputs.Count == 0 ? "{Unassigned:" + control.GetControlName() + "}" : string.Join(" or ", inputs));
        }

        /// <summary>
        /// Quick and dirty replacement for <see cref="IMyInput.IsGameControlPressed(MyStringId)"/>.
        /// </summary>
        public static bool IsControlPressed(MyStringId controlId)
        {
            IMyControl control = Input.GetGameControl(controlId);

            if(control == null)
                return false;

#if VERSION_200 || VERSION_201 || VERSION_202 || VERSION_203 || VERSION_204 || VERSION_205 // some backwards compatibility
            return control.IsPressed();
#else
            bool origEnabled = control.IsEnabled;
            try
            {
                control.IsEnabled = true;
                return control.IsPressed();
            }
            finally
            {
                control.IsEnabled = origEnabled;
            }
#endif
        }

        /// <summary>
        /// Quick and dirty replacement for <see cref="IMyInput.IsNewGameControlPressed(MyStringId)"/>.
        /// </summary>
        public static bool IsControlJustPressed(MyStringId controlId)
        {
            IMyControl control = Input.GetGameControl(controlId);

            if(control == null)
                return false;

#if VERSION_200 || VERSION_201 || VERSION_202 || VERSION_203 || VERSION_204 || VERSION_205 // some backwards compatibility
            return control.IsNewPressed();
#else
            bool origEnabled = control.IsEnabled;
            try
            {
                control.IsEnabled = true;
                return control.IsNewPressed();
            }
            finally
            {
                control.IsEnabled = origEnabled;
            }
#endif
        }
    }
}