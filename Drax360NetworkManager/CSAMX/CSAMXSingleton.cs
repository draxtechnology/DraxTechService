namespace DraxTechnology
{
    public sealed class CSAMXSingleton
    {
        private static CSAMXSingleton instance = null;
        public static CSAMX CS = new CSAMX();
        private CSAMXSingleton()
        {
        }

        public static CSAMXSingleton Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new CSAMXSingleton();
                }
                return instance;
            }
        }
    }
}
