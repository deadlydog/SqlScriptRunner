using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SQL_Script_Runner
{
	/// <summary>
	/// Class used to hold the settings to save/load.
	/// </summary>
	public class SQLScriptRunnerSettings : INotifyPropertyChanged
	{
		#region INotifyPropertyChanged
		public event PropertyChangedEventHandler PropertyChanged;
		private void NotifyPropertyChanged(String info)
		{
			if (PropertyChanged != null)
				PropertyChanged(this, new PropertyChangedEventArgs(info));
		}
		#endregion

		/// <summary>
		/// The path of the current directory holding the scripts to run.
		/// </summary>
		public string ScriptDirectory { get; set; }

		/// <summary>
		/// If true, scripts from subdirectories of the Script Directory will also be added to the list of scripts to run.
		/// </summary>
		public bool IncludeSubDirectories { get; set; }

		/// <summary>
		/// List holding all of the saved script directories.
		/// </summary>
		public ObservableCollection<string> ScriptDirectories { get; set; }

		/// <summary>
		/// The current Server IP to use.
		/// </summary>
		public string ServerIP { get; set; }

		/// <summary>
		/// List holding all saved server IPs.
		/// </summary>
		public ObservableCollection<string> ServerIPs { get; set; }

		/// <summary>
		/// The name of the database to use.
		/// </summary>
		public string DatabaseName { get; set; }

		/// <summary>
		/// Indicates if scripts that fail to run should be copied to a new directory or not.
		/// </summary>
		public bool CopyFailedScriptsToFailedDirectory { get; set; }

		/// <summary>
		/// The directory to copy scripts that fail to run to.
		/// </summary>
		public string FailedScriptsDirectory { get; set; }

		/// <summary>
		/// Tells if Integrated Security should be used to connect to the database server, instead of using a Username and Password.
		/// </summary>
		public bool UseIntegratedSecurity { get; set; }

		/// <summary>
		/// The username to connect to the database server with.
		/// </summary>
		public string Username { get; set; }

		/// <summary>
		/// The password to connect to the database server with.
		/// </summary>
		public string Password { get; set; }

		/// <summary>
		/// Constructor
		/// </summary>
		public SQLScriptRunnerSettings()
		{
			// Setup the default settings.
			ScriptDirectory = string.Empty;
			IncludeSubDirectories = false;
			ScriptDirectories = new ObservableCollection<string>();
			ServerIP = "localhost";
			ServerIPs = new ObservableCollection<string>();
			DatabaseName = string.Empty;
			CopyFailedScriptsToFailedDirectory = false;
			FailedScriptsDirectory = string.Empty;
			UseIntegratedSecurity = true;
			Username = string.Empty;
			Password = string.Empty;
		}

		/// <summary>
		/// Copies the given Settings into this instance.
		/// </summary>
		/// <param name="settingsToCopyFrom">The instance whose settings to copy.</param>
		public void CopyFrom(SQLScriptRunnerSettings settingsToCopyFrom)
		{
			this.ScriptDirectory = settingsToCopyFrom.ScriptDirectory;
			this.IncludeSubDirectories = settingsToCopyFrom.IncludeSubDirectories;
			this.ScriptDirectories = settingsToCopyFrom.ScriptDirectories;
			this.ServerIP = settingsToCopyFrom.ServerIP;
			this.ServerIPs = settingsToCopyFrom.ServerIPs;
			this.DatabaseName = settingsToCopyFrom.DatabaseName;
			this.CopyFailedScriptsToFailedDirectory = settingsToCopyFrom.CopyFailedScriptsToFailedDirectory;
			this.FailedScriptsDirectory = settingsToCopyFrom.FailedScriptsDirectory;
			this.UseIntegratedSecurity = settingsToCopyFrom.UseIntegratedSecurity;
			this.Username = settingsToCopyFrom.Username;
			this.Password = settingsToCopyFrom.Password;
		}

		/// <summary>
		/// Sends Property Changed events on all of the Settings properties
		/// </summary>
		public void NotifyThatAllPropertiesWereChanged()
		{
			NotifyPropertyChanged("ScriptDirectory");
			NotifyPropertyChanged("IncludeSubDirectories");
			NotifyPropertyChanged("ScriptDirectories");
			NotifyPropertyChanged("ServerIP");
			NotifyPropertyChanged("ServerIPs");
			NotifyPropertyChanged("DatabaseName");
			NotifyPropertyChanged("CopyFailedScriptsToFailedDirectory");
			NotifyPropertyChanged("FailedScriptsDirectory");
			NotifyPropertyChanged("UseIntegratedSecurity");
			NotifyPropertyChanged("Username");
			NotifyPropertyChanged("Password");
		}
	}
}
