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
            // TODO detect properly: escape menu, F10 and F11 menus, mission screens, yes/no notifications.

            if(Gui.ChatEntryVisible || Gui.GetCurrentScreen != MyTerminalPageEnum.None)
                return false;

            try // HACK ActiveGamePlayScreen throws NRE when called while not in a menu
            {
                return Gui.ActiveGamePlayScreen == null;
            }
            catch(Exception)
            {
                return true;
            }
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