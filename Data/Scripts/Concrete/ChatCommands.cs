using System;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;

namespace Digi.ConcreteTool
{
    public class ChatCommands
    {
        public void Register()
        {
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }

        public void Unregister()
        {
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
        }

        private void MessageEntered(string msg, ref bool send)
        {
            if(msg.StartsWith("/concrete", StringComparison.InvariantCultureIgnoreCase))
            {
                send = false;

                msg = msg.Substring("/concrete".Length).Trim();

                if(msg.StartsWith("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    ShowHelp();
                    return;
                }

                MyAPIGateway.Utilities.ShowMessage(Log.ModName, "Commands:");
                MyAPIGateway.Utilities.ShowMessage("/concrete help ", "key combination information");
            }
        }

        public void ShowHelp()
        {
            ConcreteToolMod.Instance.SeenHelp = true;

            var inputFire = InputHandler.GetAssignedGameControlNames(MyControlsSpace.PRIMARY_TOOL_ACTION);
            var inputPaint = InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_COLOR_CHANGE);
            var inputHelp = InputHandler.GetAssignedGameControlNames(MyControlsSpace.SECONDARY_TOOL_ACTION);
            var inputAlign = InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_DEFAULT_MOUNTPOINT);
            var inputSnap = InputHandler.GetAssignedGameControlNames(MyControlsSpace.FREE_ROTATION, true);
            var inputCycleMap = InputHandler.GetAssignedGameControlNames(MyControlsSpace.USE);
            string[] inputsRotation =
            {
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE, true),
                InputHandler.GetAssignedGameControlNames(MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE, true),
            };

            var str = new StringBuilder();

            str.AppendLine("The concrete tool is a hand-held tool that allows placement of concrete\n  on to asteroids or planets.");
            str.AppendLine("The tool and ammo for it can be made in an assembler.");
            str.AppendLine("While holding the tool, you must be near a planet or asteroid to use it.");
            str.AppendLine();
            str.AppendLine("Controls:");
            str.AppendLine();
            str.Append(inputFire).Append(" = place concrete.").AppendLine();
            str.AppendLine();
            str.Append(inputPaint).Append(" = replace terrain with concrete.").AppendLine();
            str.AppendLine();
            str.Append("Ctrl+").Append(inputFire).Append(" (hold) = remove terrain.").AppendLine();
            str.AppendLine();
            str.Append("Shift+MouseScroll/Plus/Minus = adjust box scale.").AppendLine();
            str.AppendLine();
            str.Append("Ctrl+MouseScroll/Plus/Minus = adjust box distance.").AppendLine();
            str.AppendLine();
            str.Append(inputAlign).Append(" = cycles alignment mode: reset alignment / align towards\n  center of asteroid/planet / align with aimed at grid.").AppendLine();
            str.AppendLine();
            str.Append(inputSnap).Append(" = cycles snap mode: no snap / snap voxel grid / snap to altitude.").AppendLine();
            str.AppendLine();
            str.Append("Shift+").Append(inputSnap).Append(" = depending on snap mode: lock to axis/plane / lock to altitude.").AppendLine();
            str.AppendLine();
            str.Append(inputCycleMap).Append(" = cycle between overlapping voxel maps.").AppendLine();
            str.AppendLine();
            str.Append(string.Join(",", inputsRotation)).Append(" = rotate the box.").AppendLine();
            str.AppendLine("  ...+Alt = rotate 1 degree increments.");
            str.AppendLine("  ...+Shift = rotate 15 degree increments.");
            str.AppendLine("  ...+Ctrl = rotate 90 degree increments.");
            str.AppendLine();
            str.Append(inputHelp).Append(" = show this window.").AppendLine();

            MyAPIGateway.Utilities.ShowMissionScreen("Concrete Tool Help", string.Empty, string.Empty, str.ToString(), null, "Close");
        }
    }
}
