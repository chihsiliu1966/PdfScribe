using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing.Design;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace PdfScribe
{
    public partial class Program
    {


        #region Message constants

        const string errorDialogCaption = "Th290 Scribe"; // Error taskdialog caption text

        const string errorDialogInstructionPDFGeneration = "There was a PDF generation error.";
        const string errorDialogInstructionCouldNotWrite = "Could not create the output file.";
        const string errorDialogInstructionUnexpectedError = "There was an internal error. Enable tracing for details.";

        const string errorDialogOutputFilenameInvalid = "Output file path is not valid. Check the \"OutputFile\" setting in the config file.";
        const string errorDialogOutputFilenameTooLong = "Output file path too long. Check the \"OutputFile\" setting in the config file.";
        const string errorDialogOutputFileAccessDenied = "Access denied - check permissions on output folder.";
        const string errorDialogTextFileInUse = "{0} is being used by another process.";
        const string errorDialogTextGhostScriptConversion = "Ghostscript error code {0}.";

        const string warnFileNotDeleted = "{0} could not be deleted.";

        #endregion

        #region Other constants
        const string traceSourceName = "PdfScribe";

        #endregion

        static TraceSource logEventSource = new TraceSource(traceSourceName);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Count() >= 1 && args[0] == "-s")
            {
                EditConfig();
                return;
            }
            // Install the global exception handler
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(Application_UnhandledException);


            String standardInputFilename = Path.GetTempFileName();
            String outputFilename = String.Empty;

            try
            {
                using (BinaryReader standardInputReader = new BinaryReader(Console.OpenStandardInput()))
                {
                    using (FileStream standardInputFile = new FileStream(standardInputFilename, FileMode.Create, FileAccess.ReadWrite))
                    {
                        standardInputReader.BaseStream.CopyTo(standardInputFile);
                    }
                }

                StripNoDistill(standardInputFilename);

                if (GetPdfOutputFilename(ref outputFilename))
                {
                    // Remove the existing PDF file if present
                    File.Delete(outputFilename);
                    // Only set absolute minimum parameters, let the postscript input
                    // dictate as much as possible
                    String[] ghostScriptArguments = { "-dBATCH", "-dNOPAUSE", "-dSAFER", "-dAutoRotatePages=/None", "-sDEVICE=pdfwrite", String.Format("-sOutputFile={0}", outputFilename), standardInputFilename, "-c", @"[/Creator(PdfScribe " + Assembly.GetExecutingAssembly().GetName().Version + " (PSCRIPT5)) /DOCINFO pdfmark", "-f" };
                    GhostScript64.CallAPI(ghostScriptArguments);
                    DisplayPdf(outputFilename);

                }
            }
            catch (IOException ioEx)
            {
                // We couldn't delete, or create a file
                // because it was in use
                logEventSource.TraceEvent(TraceEventType.Error,
                                          (int)TraceEventType.Error,
                                          errorDialogInstructionCouldNotWrite +
                                          Environment.NewLine +
                                          "Exception message: " + ioEx.Message);
                DisplayErrorMessage(errorDialogCaption,
                                    errorDialogInstructionCouldNotWrite + Environment.NewLine +
                                    String.Format("{0} is in use.", outputFilename));
            }
            catch (UnauthorizedAccessException unauthorizedEx)
            {
                // Couldn't delete a file
                // because it was set to readonly
                // or couldn't create a file
                // because of permissions issues
                logEventSource.TraceEvent(TraceEventType.Error,
                                          (int)TraceEventType.Error,
                                          errorDialogInstructionCouldNotWrite +
                                          Environment.NewLine +
                                          "Exception message: " + unauthorizedEx.Message);
                DisplayErrorMessage(errorDialogCaption,
                                    errorDialogInstructionCouldNotWrite + Environment.NewLine +
                                    String.Format("Insufficient privileges to either create or delete {0}", outputFilename));


            }
            catch (ExternalException ghostscriptEx)
            {
                // Ghostscript error
                logEventSource.TraceEvent(TraceEventType.Error,
                                          (int)TraceEventType.Error,
                                          String.Format(errorDialogTextGhostScriptConversion, ghostscriptEx.ErrorCode.ToString()) +
                                          Environment.NewLine +
                                          "Exception message: " + ghostscriptEx.Message);
                DisplayErrorMessage(errorDialogCaption,
                                    errorDialogInstructionPDFGeneration + Environment.NewLine +
                                    String.Format(errorDialogTextGhostScriptConversion, ghostscriptEx.ErrorCode.ToString()));

            }
            finally
            {
                try
                {
                    File.Delete(standardInputFilename);
                }
                catch
                {
                    logEventSource.TraceEvent(TraceEventType.Warning,
                                              (int)TraceEventType.Warning,
                                              String.Format(warnFileNotDeleted, standardInputFilename));
                }
                logEventSource.Flush();
            }
        }

        /// <summary>
        /// All unhandled exceptions will bubble their way up here - a final error dialog will be displayed before the
        /// crash and burn
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void Application_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logEventSource.TraceEvent(TraceEventType.Critical,
                                      (int)TraceEventType.Critical,
                                      ((Exception)e.ExceptionObject).Message + Environment.NewLine +
                                                                        ((Exception)e.ExceptionObject).StackTrace);
            DisplayErrorMessage(errorDialogCaption,
                                errorDialogInstructionUnexpectedError);
        }

        public class Th290ScribeOptions
        {
            [Category("Network")]
            [DisplayName("File Name")]
            [Description("Enter File Name")]
            public string OutputFile { get; set; } = "OutputFile.pdf";


            public bool _AskUserForOutputFilename = false;
            [Browsable(false)]
            public bool AskUserForOutputFilename
            {
                get => _AskUserForOutputFilename;
                set => _AskUserForOutputFilename = value;
            }
            [Category("Network")]
            [DisplayName("Server IP")]
            [Description("Enter a valid IPv4 or IPv6 address.")]
            [TypeConverter(typeof(IPAddressTypeConverter))]
            [Editor(typeof(IpAddressEditor), typeof(UITypeEditor))]
            public string IP { get; set; } = "192.168.0.100";
            [Category("Network")]
            [DisplayName("Output Location")]
            public string OutputLocation { get => $"\\\\{IP}\\PdfPrint2\\{OutputFile}"; }
        }

        static void EditConfig()
        {
            try
            {
                // Open the configuration file for the current application
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                var opt = new Th290ScribeOptions();

                if (config.AppSettings.Settings["AskUserForOutputFilename"] != null)
                {
                    bool.TryParse(config.AppSettings.Settings["AskUserForOutputFilename"].Value, out opt._AskUserForOutputFilename);
                }
                else
                {
                    // Add a new setting if it doesn't exist
                    config.AppSettings.Settings.Add("AskUserForOutputFilename", "False");
                }


                // Example: Update an existing AppSetting
                if (config.AppSettings.Settings["OutputFile"] != null)
                {
                    opt.OutputFile = Path.GetFileName(config.AppSettings.Settings["OutputFile"].Value);
                }
                else
                {
                    // Add a new setting if it doesn't exist
                    config.AppSettings.Settings.Add("OutputFile", opt.OutputFile);
                }
                if (config.AppSettings.Settings["IP"] != null)
                {
                    opt.IP = config.AppSettings.Settings["IP"].Value;
                }
                else
                {
                    // Add a new setting if it doesn't exist
                    config.AppSettings.Settings.Add("IP", opt.IP);
                }


                var f = new Form();
                f.Text = "Th290 Driver Setting";
                var grid = new PropertyGrid();
                grid.Dock = DockStyle.Fill;
                grid.SelectedObject = opt;
                f.Controls.Add(grid);
                f.ShowDialog();

                config.AppSettings.Settings["IP"].Value = opt.IP;
                config.AppSettings.Settings["OutputFile"].Value = opt.OutputLocation;
                config.AppSettings.Settings["AskUserForOutputFilename"].Value = config.AppSettings.Settings["AskUserForOutputFilename"].ToString();
                // Save the changes to the config file
                config.Save(ConfigurationSaveMode.Modified);

                // Refresh the section so the new value is available immediately
                ConfigurationManager.RefreshSection("appSettings");

                Console.WriteLine("Config updated successfully!");
            }
            catch (ConfigurationErrorsException ex)
            {
                Console.WriteLine("Error updating config: " + ex.Message);
            }
        }
    }


    public class FileSender
    {
        // Compute MD5 hash of a file
        public static string GetFileMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = md5.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        public static void Send(string filePath, string serverIp, bool deletefilePath)
        {
            //string filePath = "test.txt"; // File to send
            //string serverIp = "127.0.0.1";
            int port = 5000;

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found.");
                return;
            }

            try
            {
                using (TcpClient client = new TcpClient(serverIp, port))
                using (NetworkStream ns = client.GetStream())
                {
                    // 1. Send file name
                    string fileName = Path.GetFileName(filePath);
                    byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                    ns.Write(BitConverter.GetBytes(fileNameBytes.Length), 0, 4);
                    ns.Write(fileNameBytes, 0, fileNameBytes.Length);

                    // 2. Send MD5
                    string md5Hash = GetFileMD5(filePath);
                    byte[] md5Bytes = Encoding.UTF8.GetBytes(md5Hash);
                    ns.Write(BitConverter.GetBytes(md5Bytes.Length), 0, 4);
                    ns.Write(md5Bytes, 0, md5Bytes.Length);

                    // 3. Send file size
                    long fileSize = new FileInfo(filePath).Length;
                    ns.Write(BitConverter.GetBytes(fileSize), 0, 8);

                    // 4. Send file content
                    using (FileStream fs = File.OpenRead(filePath))
                    {
                        fs.CopyTo(ns);
                    }

                    Console.WriteLine($"File '{fileName}' sent with MD5: {md5Hash}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending file: " + ex.Message);
            }
            finally
            {
                if (deletefilePath)
                    File.Delete(filePath);
            }
        }
    }


    public class FileReceiver
    {
        public static string GetFileMD5(string filePath)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = md5.ComputeHash(stream);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        public static void Receive()
        {
            int port = 5000;
            string saveDir = "ReceivedFiles";
            Directory.CreateDirectory(saveDir);

            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Console.WriteLine("Waiting for file...");

                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream ns = client.GetStream())
                {
                    // 1. Read file name
                    byte[] intBuffer = new byte[4];
                    ns.Read(intBuffer, 0, 4);
                    int fileNameLen = BitConverter.ToInt32(intBuffer, 0);
                    byte[] fileNameBytes = new byte[fileNameLen];
                    ns.Read(fileNameBytes, 0, fileNameLen);
                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    // 2. Read MD5
                    ns.Read(intBuffer, 0, 4);
                    int md5Len = BitConverter.ToInt32(intBuffer, 0);
                    byte[] md5Bytes = new byte[md5Len];
                    ns.Read(md5Bytes, 0, md5Len);
                    string sentMD5 = Encoding.UTF8.GetString(md5Bytes);

                    // 3. Read file size
                    byte[] longBuffer = new byte[8];
                    ns.Read(longBuffer, 0, 8);
                    long fileSize = BitConverter.ToInt64(longBuffer, 0);

                    // 4. Read file content
                    string savePath = Path.Combine(saveDir, fileName);
                    using (FileStream fs = File.Create(savePath))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;
                        while (totalRead < fileSize &&
                               (bytesRead = ns.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            fs.Write(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }
                    }

                    // 5. Verify MD5
                    string receivedMD5 = GetFileMD5(savePath);
                    if (receivedMD5 == sentMD5)
                        Console.WriteLine($"File '{fileName}' received successfully. MD5 verified.");
                    else
                        Console.WriteLine($"File '{fileName}' received but MD5 mismatch!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error receiving file: " + ex.Message);
            }
        }
    }


    // Custom TypeConverter to validate IP address strings
    public class IPAddressTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
            {
                s = s.Trim();
                if (IPAddress.TryParse(s, out _))
                {
                    return s; // Valid IP string
                }
                throw new FormatException("Invalid IP address format.");
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
    // 1️⃣ Custom UITypeEditor for IP address editing
    public class IpAddressEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            // Show a modal dialog when editing
            return UITypeEditorEditStyle.Modal;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            IWindowsFormsEditorService editorService =
                provider?.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;

            if (editorService != null)
            {
                using (Form form = new Form())
                {
                    form.Text = "Enter IP Address";
                    form.FormBorderStyle = FormBorderStyle.FixedDialog;
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.ClientSize = new System.Drawing.Size(250, 80);
                    form.MinimizeBox = false;
                    form.MaximizeBox = false;

                    // MaskedTextBox for IP format
                    MaskedTextBox ipBox = new MaskedTextBox("990.990.990.990")
                    {
                        Text = value?.ToString() ?? "",
                        Location = new System.Drawing.Point(20, 20),
                        Width = 200
                    };
                    form.Controls.Add(ipBox);

                    Button okButton = new Button()
                    {
                        Text = "OK",
                        DialogResult = DialogResult.OK,
                        Location = new System.Drawing.Point(50, 50)
                    };
                    form.Controls.Add(okButton);

                    Button cancelButton = new Button()
                    {
                        Text = "Cancel",
                        DialogResult = DialogResult.Cancel,
                        Location = new System.Drawing.Point(130, 50)
                    };
                    form.Controls.Add(cancelButton);

                    form.AcceptButton = okButton;
                    form.CancelButton = cancelButton;

                    if (editorService.ShowDialog(form) == DialogResult.OK)
                    {
                        string ipText = ipBox.Text.Trim();
                        if (IPAddress.TryParse(ipText, out _))
                        {
                            return ipText;
                        }
                        else
                        {
                            MessageBox.Show("Invalid IP address format.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            return value; // Return original if canceled or invalid
        }
    }
}
