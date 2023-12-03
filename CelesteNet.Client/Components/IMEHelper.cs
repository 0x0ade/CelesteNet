using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Components
{
    using System;
    using System.Drawing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Windows.Forms;
    using static System.Net.Mime.MediaTypeNames;


    public static class IMEHelper
    {
        static bool createdInput = false;

        public static bool isCore = isUsingCore();
        public static bool isFNA = isUsingFNA();

        public static bool isUsingFNA()
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BuildIsFNA.txt");
            return File.Exists(filePath);
        }
        public static bool isUsingCore()
        {

            var name = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<TargetFrameworkAttribute>()?
                .FrameworkName;
            return name.StartsWith(".NETCore");
        }

        public static void InitLog()
        {
            Logger.Log(LogLevel.WRN, "IMEHelper", $"::: InitInfo ::: - IsUsingCore: {isCore} - IsFNA: {isFNA}");
        }
        public static bool fixIMEForCeleste()
        {
            if (isCore) return false;



            if (!createdInput)
            {
                createdInput = true;
                CreateInvisibleFormWithInputBox();

                return true;
            }

            return false;
        }

        public delegate void SetTextBoxFocus(bool focused);

        private static SetTextBoxFocus handler;

        public static SetTextBoxFocus getFocusHandler()
        {
            if (isFNA) return b => { };
            if (handler == null)
            {
                handler = CreateFormWithInputBoxForceFocus();
            }
            return handler;
        }

        static SetTextBoxFocus CreateFormWithInputBoxForceFocus()
        {
            string result = String.Empty;

            IntPtr celesteHandle = NativeMethods.FindWindow(null, "Celeste");
            if (celesteHandle == IntPtr.Zero)
            {
                return null;
            }

            Form invisibleForm = new Form()
            {
                Width = 0,
                Height = 0,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.None,
                Opacity = 1
            };

            TextBox textBox = new TextBox()
            {
                Location = new Point(0, 0),
                Size = new Size(0, 0),
                TabStop = false
            };
            invisibleForm.Controls.Add(textBox);

            invisibleForm.Shown += (sender, e) => {
                invisibleForm.Opacity = 1;
                textBox.Size = new Size(0, 0);
                textBox.Multiline = true;
            };

            NativeMethods.SetParent(invisibleForm.Handle, celesteHandle);
            NativeMethods.SetWindowPos(invisibleForm.Handle, IntPtr.Zero, -100, -100, 0, 0, SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);


            Task.Run(() => {
                Task.Delay(100).ContinueWith((t) => {
                    // NativeMethods.SetFocus(celesteHandle);
                });
                System.Windows.Forms.Application.Run(invisibleForm);
            });

            return (focused) => {
                if (focused)
                {
                    textBox.Focus();
                    textBox.Text = "";
                }
                else
                {
                    NativeMethods.SetFocus(celesteHandle);
                }
            };
        }


        static void CreateInvisibleFormWithInputBox()
        {
            string result = String.Empty;

            IntPtr celesteHandle = NativeMethods.FindWindow(null, "Celeste");
            if (celesteHandle == IntPtr.Zero)
            {
                return;
            }

            Form invisibleForm = new Form()
            {
                Width = 0,
                Height = 0,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.None,
                Opacity = 0
            };

            TextBox textBox = new TextBox()
            {
                Location = new Point(0, 0),
                Size = new Size(0, 0),
                TabStop = false
            };
            invisibleForm.Controls.Add(textBox);

            invisibleForm.Shown += (sender, e) => {
                invisibleForm.Opacity = 0;
                textBox.Size = new Size(0, 0);
                textBox.Focus();
            };

            NativeMethods.SetParent(invisibleForm.Handle, celesteHandle);
            NativeMethods.SetWindowPos(invisibleForm.Handle, IntPtr.Zero, 0, 0, 0, 0, SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);


            Task.Run(() => {
                Task.Delay(100).ContinueWith((t) => {
                    NativeMethods.SetFocus(celesteHandle);
                });
                System.Windows.Forms.Application.Run(invisibleForm);
            });
        }


        internal static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, SetWindowPosFlags uFlags);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SetFocus(IntPtr hWnd);
        }

        [Flags]
        internal enum SetWindowPosFlags : uint
        {
            SWP_NOSIZE = 0x0001,
            SWP_NOZORDER = 0x0004,
            SWP_NOACTIVATE = 0x0010
        }
    }

}
