namespace DraxTechnology
{
    partial class DraxService
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            //
            // DraxService
            //
            // Must exactly match the ServiceInstall/@Name WiX registers this instance's
            // install under (Product.wxs) — SCM dispatches to a running process by this
            // string, so a mismatch here means the service never starts. Suffixed with
            // the resolved AmxInstance so each of the 4 side-by-side installs answers to
            // its own registered name (DraxTechnology1..4); also doubles as the
            // Application-log event source (ServiceBase.EventLog uses ServiceName).
            this.ServiceName = "DraxTechnology" + AMXTransfer.ReadConfiguredInstanceNumber();

        }

        #endregion
    }
}
