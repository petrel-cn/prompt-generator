using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace PromptGenerator
{
    /// <summary>
    /// 程序入口：TLS 初始化、单实例互斥、全局异常兜底。
    /// </summary>
    internal static class Program
    {
        private const string MutexName = @"Local\PromptGenerator_SingleInstance";

        [STAThread]
        private static void Main()
        {
            // .NET Framework 默认可能协商 TLS 1.0，DeepSeek 会拒绝
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // Tls12
            }
            catch (Exception)
            {
                // 极端环境下不支持时忽略，后续由请求报错反馈
            }

            bool createdNew;
            Mutex mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("提示词生成器已在运行。", "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                Storage.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化数据目录失败：\r\n" + ex.Message, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
            }
            finally
            {
                GC.KeepAlive(mutex);
                mutex.ReleaseMutex();
                mutex.Close();
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowFatal(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatal(e.ExceptionObject as Exception);
        }

        private static void ShowFatal(Exception ex)
        {
            string message = ex == null ? "发生未知错误。" : ex.Message;
            try
            {
                MessageBox.Show("程序发生错误：\r\n" + message, "绘图提示词生成器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
                // 弹窗失败时无可为
            }
        }
    }
}
