using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    /// <summary>Welcome board behind the elevator: how to play, controls and the responsible-play promise.</summary>
    public class WelcomeBoard : MonoBehaviour
    {
        void Start()
        {
            var p = UIKit.Panel("Welcome", transform, new Vector3(0, 2.0f, 0), Quaternion.identity, new Vector2(1300, 900), 2.6f, Palette.Violet);
            UIKit.Label(p, "WELCOME TO ASCEND", 40, 20, 1220, 100, 92, Palette.Text, Palette.Display);
            UIKit.Label(p, "A FICTIONAL GAMER CASINO · 18+ · PLAY CHIPS ONLY · NO REAL MONEY", 40, 120, 1220, 40, 24, Palette.Gold, Palette.Mono);
            UIKit.Label(p,
                "<b>HANDS</b>  grip = make a fist · trigger = curl your index · point (grip, no trigger) at anyone to see their player card\n" +
                "<b>TELEPORT</b>  look at the back of your RIGHT forearm > floor menu · or use the elevator panels\n" +
                "<b>MENU</b>  left MENU button (or Y / B): wallet & deposits, profile, missions, settings, online + voice\n" +
                "<b>BET</b>  grab chips from the tray and drop them on the felt · or use the quick-bet panel\n" +
                "<b>PLAY</b>  press the glowing buttons with your finger · pull levers · shake cups and boxes · cast the rod\n" +
                "<b>FLOORS</b>  7 floors: Classics, Fast Rounds, Luck & Loot, Case Battles, Sky Lounge, Rooftop Raid\n\n" +
                "<color=#9A94B0>Play for fun with play chips. Set a loss limit, take breaks, and if gambling stops being fun: jugarbien.es</color>",
                40, 180, 1220, 680, 30, Palette.Text, Palette.Body, TextAlignmentOptions.TopLeft);
        }
    }
}
