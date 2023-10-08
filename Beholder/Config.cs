using System.Globalization;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using TShockAPI;

namespace Beholder;

public class Config
{
    public bool NotifyToOnlineAdmin;
    public bool NotifyToConsoleLogs;
    public bool NotifyAllPlayer;
    public bool SaveToLogFile;
    public int StackCheckThreshold;
    public List<string> ListAdminGroupName;
    public List<int> ExcludedItemId;

    //TODO incomplete config
    public Config()
    {
        var path = Path.Combine(TShock.SavePath, "Beholder.json");
        if (File.Exists(path)) return;
        Default();
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public void ReadConfig()
    {
        var path = Path.Combine(TShock.SavePath, "Beholder.json");
        if (File.Exists(path))
        {
            var c = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
            NotifyToOnlineAdmin = c.NotifyToOnlineAdmin;
            NotifyToConsoleLogs = c.NotifyToConsoleLogs;
            NotifyAllPlayer = c.NotifyAllPlayer;
            SaveToLogFile = c.SaveToLogFile;
            StackCheckThreshold = c.StackCheckThreshold;
            ListAdminGroupName = c.ListAdminGroupName;
            ExcludedItemId = c.ExcludedItemId;
        }
        else
        {
            TShock.Utils.SendLogs("Config file cannot be accessed, reverting to default config value.", Color.Red);
            Default();
        }
    }

    private void Default()
    {
        NotifyToOnlineAdmin = true;
        NotifyToConsoleLogs = true;
        NotifyAllPlayer = false;
        SaveToLogFile = false;
        StackCheckThreshold = 999;
        ListAdminGroupName = new List<string> {"admin", "newadmin", "trustedadmin", "superadmin", "owner"};
        ExcludedItemId = new List<int> {  };
    }

    public void AppendLogs(string Msg)
    {
        var date = DateTime.Today.Date.ToString(CultureInfo.CurrentCulture);
        var path = Path.Combine(TShock.SavePath, $"BeholderLog-{date}.txt");
        if (File.Exists(path))
            File.AppendAllText(path, DateTime.Now.ToString(CultureInfo.CurrentCulture) + Msg + Environment.NewLine);
        else File.WriteAllText(path, DateTime.Now.ToString(CultureInfo.CurrentCulture) + Msg + Environment.NewLine);
    }
}