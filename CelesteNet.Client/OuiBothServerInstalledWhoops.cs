using System.Collections;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client
{
    public class OuiBothServerInstalledWhoops : Oui
    {
        private readonly TextMenu menu;

        public OuiBothServerInstalledWhoops()
        {
            // TODO 其他更好的 "clean" 方法?
            string des = Dialog.Get("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS").Replace("{break}", "\n") + "\n";
            var module = CelesteNetClientModule.Instance;
            des = string.Format(des, module.CurrentVersion, module.CurrentCelesteNetVersion);

            var btn1 = new TextMenu.Button(Dialog.Clean("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS_DISABLE")).Pressed(UseGroupServerAndRestart);
            var btn2 = new TextMenu.Button(Dialog.Clean("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS_CONTINUE")).Pressed(Return);
            menu = new TextMenu()
        {
            new TextMenu.Header("Whoops!"),
            new TextMenu.SubHeader(Dialog.Clean("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS_WHAT")),
            new TextMenu.SubHeader(des),
            btn1,
            btn2,
        };
            btn1.AddDescription(menu, Dialog.Clean("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS_DISABLE_DESC"));
            btn2.AddDescription(menu, Dialog.Clean("MIAOCELESTENET_BOTHSERVERINSTALLEDWHOOPS_CONTINUE_DESC"));
        }

        public override IEnumerator Enter(Oui from)
        {
            Overworld.Maddy.Hide();
            Scene.Add(menu);
            TweenInMenu(menu);
            yield return 0.25f;
            Focused = true;
            yield break;
        }

        public override IEnumerator Leave(Oui next)
        {
            TweenOutMenu(menu);
            yield return 0.25f;
            Focused = false;
            CelesteNetClientModule.Instance.ShownInstalledBothServerWarning = true;
            yield break;
        }

        public void TweenInMenu(TextMenu menu)
        {
            var from = menu.X - 100f;
            var to = menu.X;
            Tween.Set(menu, Tween.TweenMode.Oneshot, 0.25f, Ease.QuadInOut, t =>
            {
                menu.Alpha = MathHelper.Lerp(0f, 1f, t.Eased);
                menu.X = MathHelper.Lerp(from, to, t.Eased);
            });
        }

        public void TweenOutMenu(TextMenu menu)
        {
            var from = menu.X;
            var to = menu.X + 100f;
            Tween.Set(menu, Tween.TweenMode.Oneshot, 0.25f, Ease.QuadInOut, t =>
            {
                menu.Alpha = MathHelper.Lerp(1f, 0f, t.Eased);
                menu.X = MathHelper.Lerp(from, to, t.Eased);
            }).OnComplete = t =>
            {
                Scene.Remove(menu);
            };
        }

        public void Return()
        {
            Overworld.Goto<OuiMainMenu>();
        }

        public void UseGroupServerAndRestart()
        {
            var list = Everest.Loader.Blacklist.ToList();
            var modules = Everest.Modules;
            var celesteNet = modules.First(m => m.Metadata.Name == "CelesteNet.Client");
            var fileName = Path.GetFileName(celesteNet.Metadata.PathArchive);
            list.Add(fileName);
            using StreamWriter sw = new(Everest.Loader.PathBlacklist);
            sw.WriteLine("# This is the blacklist. Lines starting with # are ignored.");
            sw.WriteLine("# File generated through the Miao.CelesteNet.Client \"Whoops\" menu.");
            foreach (var fn in list)
            {
                sw.WriteLine(fn);
            }
            sw.Close();
            Everest.QuickFullRestart();
        }
    }
}