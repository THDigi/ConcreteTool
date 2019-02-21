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
    }
}