using System;
using System.Collections.Generic;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using VRage.Game.ModAPI;
using static Sandbox.ModAPI.MyAPIGateway;

namespace Digi
{
    public static class InputHandler
    {
        public static bool IsInputReadable()
        {
            return !Gui.IsCursorVisible && !Gui.ChatEntryVisible;
        }

        public static string GetAssignedGameControlNames(MyStringId controlId)
        {
            return GetAssignedGameControlNames(Input.GetGameControl(controlId));
        }

        public static string GetAssignedGameControlNames(IMyControl control)
        {
            var inputs = new List<string>();

            if(control.GetMouseControl() != MyMouseButtonsEnum.None)
                inputs.Add(Input.GetName(control.GetMouseControl()));

            if(control.GetKeyboardControl() != MyKeys.None)
                inputs.Add(Input.GetKeyName(control.GetKeyboardControl()));

            if(control.GetSecondKeyboardControl() != MyKeys.None)
                inputs.Add(Input.GetKeyName(control.GetSecondKeyboardControl()));

            return (inputs.Count == 0 ? Input.GetUnassignedName() : string.Join(" or ", inputs));
        }
    }
}