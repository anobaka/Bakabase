namespace Bakabase.InsideWorld.App.WinForms
{
    public partial class FirstTimeExitDialog : Form
    {
        private bool _remember;

        public event Action<OperationType, bool>? OnOperate;

        public FirstTimeExitDialog()
        {
            InitializeComponent();

            this.minimizeBtn.Click += (sender, args) =>
            {
                OnOperate?.Invoke(OperationType.Minimize, _remember);
            };

            this.exitBtn.Click += (sender, args) =>
            {
                OnOperate?.Invoke(OperationType.Exit, _remember);
            };

            this.rememberCheckBox.CheckedChanged += (sender, args) =>
            {
                _remember = this.rememberCheckBox.Checked;
            };
        }

        public enum OperationType
        {
            Minimize = 1,
            Exit = 2
        }
    }
}
