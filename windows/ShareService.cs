using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace NotchZero
{
    /// <summary>
    /// Opens the Windows Share sheet for files (Nearby Share, Outlook, WhatsApp, Teams…) —
    /// the closest thing Windows has to AirDrop.
    /// </summary>
    internal static class ShareService
    {
        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        // IID of Windows.ApplicationModel.DataTransfer.IDataTransferManager
        private static readonly Guid DataTransferManagerIid = new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll", ExactSpelling = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, [In] ref Guid iid, out IntPtr factory);

        private static IDataTransferManagerInterop GetInterop()
        {
            const string className = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out IntPtr hstring));
            try
            {
                var iid = typeof(IDataTransferManagerInterop).GUID;
                Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out IntPtr factory));
                try
                {
                    return (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory);
                }
                finally
                {
                    Marshal.Release(factory);
                }
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        public static async Task<bool> ShareFilesAsync(IntPtr hwnd, IReadOnlyList<string> paths)
        {
            try
            {
                var items = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    if (File.Exists(path)) items.Add(await StorageFile.GetFileFromPathAsync(path));
                    else if (Directory.Exists(path)) items.Add(await StorageFolder.GetFolderFromPathAsync(path));
                }
                if (items.Count == 0) return false;

                var interop = GetInterop();
                var iid = DataTransferManagerIid;
                IntPtr ptr = interop.GetForWindow(hwnd, ref iid);
                var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);

                TypedEventHandler<DataTransferManager, DataRequestedEventArgs> handler = null;
                handler = (sender, args) =>
                {
                    sender.DataRequested -= handler;
                    var data = args.Request.Data;
                    data.Properties.Title = items.Count == 1 ? items[0].Name : $"{items.Count} items";
                    data.Properties.Description = "Shared with NotchZero";
                    data.SetStorageItems(items);
                };
                manager.DataRequested += handler;
                interop.ShowShareUIForWindow(hwnd);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Share");
                return false;
            }
        }
    }
}
