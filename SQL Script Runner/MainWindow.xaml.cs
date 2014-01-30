using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Diagnostics;

namespace SQL_Script_Runner
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window, INotifyPropertyChanged
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
		/// Holds the current settings.
		/// </summary>
		public SQLScriptRunnerSettings Settings { get; set; }

		/// <summary>
		/// Path to the settings file to save and load.
		/// </summary>
		private string SETTINGS_FILE_PATH = null;

		/// <summary>
		/// List holding all saved database names.
		/// </summary>
		public ObservableCollection<string> DatabaseNames { get; set; }

		/// <summary>
		/// Holds the value of the last valid Database Name that was selected
		/// </summary>
		private string _lastDatabaseName { get; set; }

		/// <summary>
		/// Holds the value of the last Server IP we tried to connect to
		/// </summary>
		private string _lastServerIP { get; set; }

		/// <summary>
		/// Holds whether the current request to get Database Names is a Forced Connect or not.
		/// </summary>
		private bool _forcedConnectToRetrieveDatabaseNames { get; set; }

		BackgroundWorker _scriptRunnerWorker = null;
		BackgroundWorker _databaseNameWorker = null;

		/// <summary>
		/// Key used to encrypt/decrypt the password for storage in the Settings file.
		/// </summary>
		private const string ENCRYPT_DECRYPT_KEY = "SQLScriptRunner";

		/// <summary>
		/// Class used to pass settings into the Script Runner background worker.
		/// </summary>
		class ScriptRunnerWorkerSettings
		{
			/// <summary>
			/// The IP address of the SQL server to connect to.
			/// </summary>
			public string ServerIP = string.Empty;

			/// <summary>
			/// The SQL database to run against.
			/// </summary>
			public string DatabaseName = string.Empty;

			/// <summary>
			/// Tells if we should connect to the SQL server using Integrated Security or not.
			/// </summary>
			public bool UseIntegreatedSecurity = true;

			/// <summary>
			/// The username to connect with if we are not using Integrated Security.
			/// </summary>
			public string Username = string.Empty;

			/// <summary>
			/// The password to connect with if we are not using Integrated Security.
			/// </summary>
			public string Password = string.Empty;

			/// <summary>
			/// A list of all of the SQL script files to run.
			/// </summary>
			public List<FileInfo> ScriptsToRun = new List<FileInfo>();

			/// <summary>
			/// If true, any scripts that fail to run will be copied to the Failed Scripts Directory.
			/// </summary>
			public bool CopyFailedScriptsToFailedDirectory = false;

			/// <summary>
			/// The directory to copy any scripts that fail to. If null/empty a new "Failed" directory will be created in the same directory as the script.
			/// </summary>
			public string FailedScriptsDirectory = string.Empty;

			/// <summary>
			/// If true, only scripts that contain "create procedure", "alter procedure", "create function", or "alter function" will run.
			/// </summary>
			public bool OnlyRunProcedureAndFunctionScripts = true;

			/// <summary>
			/// True if the Force Connect button was pressed to get the Database Server names.
			/// </summary>
			public bool ForceConnect = false;
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="MainWindow"/> class.
		/// </summary>
		public MainWindow()
		{
			InitializeComponent();
			this.DataContext = this;

			// Initialize the settings
			this.Settings = new SQLScriptRunnerSettings();
			DatabaseNames = new ObservableCollection<string>();

			// Set the paths where the Data to save/load can be found.
			SETTINGS_FILE_PATH = Directory.GetCurrentDirectory() + "\\SqlScriptRunnerSettings.xml";

			_scriptRunnerWorker = new BackgroundWorker();
			_scriptRunnerWorker.DoWork += new DoWorkEventHandler(_scriptRunnerWorker_DoWork);
			_scriptRunnerWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(_scriptRunnerWorker_RunWorkerCompleted);

			SetupNewDatabaseNameWorker();

			// Load the settings
			LoadSettings();
		}

		/// <summary>
		/// Setup a new Database Name Worker
		/// </summary>
		private void SetupNewDatabaseNameWorker()
		{
			_databaseNameWorker = new BackgroundWorker();
			_databaseNameWorker.WorkerSupportsCancellation = true;
			_databaseNameWorker.DoWork += new DoWorkEventHandler(_databaseNameWorker_DoWork);
			_databaseNameWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(_databaseNameWorker_RunWorkerCompleted);
		}

		/// <summary>
		/// Saves the settings to the given File Path.
		/// <para>Throws an Exception if error occurs while saving the settings.</para>
		/// </summary>
		/// <returns>Returns true if the save was successful, false if not.</returns>
		public bool SaveSettings()
		{
			// Reorder the combo boxes alphabetically before saving.
			List<string> directories = Settings.ScriptDirectories.OrderBy(x => x.ToString()).ToList<string>();
			List<string> servers = Settings.ServerIPs.OrderBy(x => x.ToString()).ToList<string>();
			Settings.ScriptDirectories.Clear();
			Settings.ServerIPs.Clear();
			foreach (string directory in directories)
				Settings.ScriptDirectories.Add(directory);
			foreach (string server in servers)
				Settings.ServerIPs.Add(server);

			// Only save the password if Remember Password is checked, and encrypt it before saving it to the file.
			if (chkRememberPassword.IsChecked ?? false)
				Settings.Password = Crypto.EncryptStringAES(pasPassword.Password, ENCRYPT_DECRYPT_KEY);
			else
				Settings.Password = string.Empty;

			TextWriter writer = null;
			try
			{
				// Serialize the settings into XML format.
				XmlSerializer serializer = new XmlSerializer(typeof(SQLScriptRunnerSettings));
				writer = new StreamWriter(SETTINGS_FILE_PATH);
				serializer.Serialize(writer, this.Settings);
			}
			catch (Exception ex)
			{
				// Display the error message and return that the save was not successful.
				MessageBox.Show("There was a problem saving the settings to " + SETTINGS_FILE_PATH + ".\n\n" + ex.Message, "Error Saving Settings");
				return false;
			}
			finally
			{
				// Release our handle to the file
				if (writer != null)
					writer.Close();
			}

			// Return success
			return true;
		}

		/// <summary>
		/// Loads the settings from the given File Path.
		/// <para>Throws an Exception if error occurs while loading the settings.</para>
		/// </summary>
		/// <returns>Returns true if the settings were loaded successfully, false if not.</returns>
		public bool LoadSettings()
		{
			// If the Settings file to load does not exist
			if (!File.Exists(SETTINGS_FILE_PATH))
			{
				// Get the list of database names from the default server, and then exit without loading any other settings.
				GetDatabaseNames();

				// Show the default settings in the UI.
				UpdateUIToReflectSettingsValuesForPropertiesNotUsingBinding();

				return false;
			}

			TextReader reader = null;
			try
			{
				// De-serialize the settings.
				XmlSerializer serializer = new XmlSerializer(typeof(SQLScriptRunnerSettings));
				reader = new StreamReader(SETTINGS_FILE_PATH);
				SQLScriptRunnerSettings data = (SQLScriptRunnerSettings)serializer.Deserialize(reader);

				// Copy the loaded data into this class instance
				Settings.CopyFrom(data);
			}
			catch (Exception ex)
			{
				// Display the error message and return that the load was not successful.
				MessageBox.Show("There was a problem loading the settings from " + SETTINGS_FILE_PATH + ".\n\n" + ex.Message, "Error Loading Settings");
				return false;
			}
			finally
			{
				// Release our handle to the file
				if (reader != null)
					reader.Close();
			}

			// Set the default database name to use
			_lastDatabaseName = Settings.DatabaseName;

			// Get the list of database names available on the specified server
			GetDatabaseNames();

			// If a password was loaded from the Settings
			if (!string.IsNullOrEmpty(Settings.Password))
			{
				// Remember Password must have been checked to save the password, so make sure it is checked again now that we've loaded the password.
				chkRememberPassword.IsChecked = true;

				// Get the password.
				Settings.Password = Crypto.DecryptStringAES(Settings.Password, ENCRYPT_DECRYPT_KEY);
			}

			// Show the settings being used on the UI.
			UpdateUIToReflectSettingsValuesForPropertiesNotUsingBinding();

			// Refresh the scripts list to show any scripts in the already chosen directory
			RefreshScriptsList();

			// Update any bindings
			NotifyThatAllPropertiesWereChanged();

			// Return success
			return true;
		}

		/// <summary>
		/// Updates the UI to reflect the Settings' values.
		/// </summary>
		private void UpdateUIToReflectSettingsValuesForPropertiesNotUsingBinding()
		{
			// Set the combo box text
			cboServerIPs.Text = Settings.ServerIP;
			cboDatabaseNames.Text = Settings.DatabaseName;
			cboScriptDirectories.Text = Settings.ScriptDirectory;
			pasPassword.Password = Settings.Password;
		}

		/// <summary>
		/// Notifies any bindings that all properties were changed.
		/// </summary>
		private void NotifyThatAllPropertiesWereChanged()
		{
			Settings.NotifyThatAllPropertiesWereChanged();
			NotifyPropertyChanged("DatabaseNames");
		}

		/// <summary>
		/// Handles the Click event of the btnRunScripts control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnRunScripts_Click(object sender, RoutedEventArgs e)
		{
			// If any of the settings are not valid, tell the user and exit.
			if (string.IsNullOrWhiteSpace(Settings.ServerIP))
			{
				MessageBox.Show("You must specify a proper server to connect to.", "Can Not Run");
				return;
			}
			if (string.IsNullOrWhiteSpace(Settings.DatabaseName))
			{
				MessageBox.Show("You must pick database to run the SQL scripts against.", "Can Not Run");
				return;
			}
			if (listScriptsToRun.Items.Count <= 0)
			{
				MessageBox.Show("There are no SQL scripts specified to be ran.", "Can Not Run");
				return;
			}

			// Create the list of scripts to run
			List<FileInfo> scriptFiles = new List<FileInfo>();
			foreach (FileInfo script in listScriptsToRun.Items)
			{
				scriptFiles.Add(script);
			}

			// Get the settings to pass to the worker.
			var workerSettings = GetScriptRunnerWorkerSettings(scriptFiles);

			// Disable the Output window while running, and run the scripts.
			DisableControlsWhileRunningScripts();
			_scriptRunnerWorker.RunWorkerAsync(workerSettings);
		}

		/// <summary>
		/// Create the settings to pass to the background worker
		/// </summary>
		private ScriptRunnerWorkerSettings GetScriptRunnerWorkerSettings(List<FileInfo> scriptFiles = null)
		{
			ScriptRunnerWorkerSettings workerSettings = new ScriptRunnerWorkerSettings();
			workerSettings.ServerIP = Settings.ServerIP;
			workerSettings.DatabaseName = Settings.DatabaseName;
			workerSettings.UseIntegreatedSecurity = Settings.UseIntegratedSecurity;
			workerSettings.Username = Settings.Username;
			workerSettings.Password = Settings.Password;
			workerSettings.ScriptsToRun = scriptFiles;
			workerSettings.CopyFailedScriptsToFailedDirectory = Settings.CopyFailedScriptsToFailedDirectory;
			workerSettings.FailedScriptsDirectory = Settings.FailedScriptsDirectory;
			workerSettings.OnlyRunProcedureAndFunctionScripts = chkOnlyRunSprocsAndFunctions.IsChecked ?? false;
			return workerSettings;
		}

		/// <summary>
		/// Applies the stored procedures to DB.
		/// </summary>
		/// <param name="settings">The settings to use.</param>
		private string ApplyStoredProceduresToDB(ScriptRunnerWorkerSettings settings)
		{
			StringBuilder output = new StringBuilder();
			StringBuilder scriptsFailed = new StringBuilder();
			StringBuilder scriptsSkipped = new StringBuilder();

			string failedTemp = string.Empty;
			string skippedTemp = string.Empty;

			int numberOfScriptsThatFailed = 0;
			int numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions = 0;

			int numberOfScriptsToRun = settings.ScriptsToRun.Count;
			int numberOfScriptsRan = 0;

			// Start a timer to log how long the operation takes.
			Stopwatch timerToRunAllScripts = Stopwatch.StartNew();

			// Display when we started running the scripts in the status bar.
			var timeScriptsStartedRunning = DateTime.Now;
			this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, (System.Threading.ThreadStart)delegate()
			{
				sbtxtTimeStartedRunningScripts.Text = string.Format("{0}", timeScriptsStartedRunning.ToLongTimeString());
				sbtxtElapsedTimeOfRunningScripts.Text = "0 seconds";	// Start the elapsed time showing 0 seconds.
			});

			// Start a timer to display how long the scripts have been running for, and have it update once every second.
			var elapsedTimer = new System.Threading.Timer((x) =>
			{
				this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, (System.Threading.ThreadStart)delegate()
				{
					var elapsedTime = DateTime.Now - timeScriptsStartedRunning;
					string elapsedTimeText = string.Empty;
					if (elapsedTime.Minutes > 0)
						elapsedTimeText += elapsedTime.Minutes.ToString() + " minute(s) and ";
					elapsedTimeText += (elapsedTime.TotalSeconds - (elapsedTime.Minutes * 60)).ToString("#") + " seconds";
					sbtxtElapsedTimeOfRunningScripts.Text = elapsedTimeText;
				});
			}, null, 1000, 1000);

			// Setup our connection and connect to the server
			var serverConnection = new ServerConnection();
			serverConnection.ConnectionString = string.Format("Data Source={0};Integrated Security={1};User ID={2};Password={3};Connect Timeout=30;Initial Catalog={4};", 
				settings.ServerIP, settings.UseIntegreatedSecurity ? "true" : "false", settings.Username, settings.Password, settings.DatabaseName);
			var server = new Server(serverConnection);
			var db = server.Databases[server.ConnectionContext.SqlConnectionObject.Database];
			output.AppendLine("Connection Info: " + serverConnection.ConnectionString.Replace("Password=" + settings.Password, "Password=[hidden]") + "\n");
			output.AppendLine("=================================================================");
			output.AppendLine("All Script Summaries (in the order that the scripts were ran)");
			output.AppendLine("=================================================================\n");
			
			// Apply all of the scripts
			foreach (FileInfo file in settings.ScriptsToRun)
			{
				var fileName = file.Name;
				var filePath = file.FullName;
				numberOfScriptsRan++;

				// Update the status bar to show our progress.
				string numberOfScriptsRanText = string.Format("{0} of {1}", numberOfScriptsRan, numberOfScriptsToRun);
				this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, (System.Threading.ThreadStart)delegate()
				{
					sbtxtNumberOfScriptsRan.Text = numberOfScriptsRanText;
					sbtxtNameOfCurrentlyRunningScript.Text = fileName;
					txtOutput.Text = output.ToString().Trim();
					txtOutput.ScrollToEnd();
				});

				// If the file doesn't exist, record it and move onto the next one
				if (!File.Exists(filePath))
				{
					failedTemp = filePath + ": FILE DOES NOT EXIST\n";
					scriptsFailed.Append(failedTemp);
					output.Append(failedTemp + "--------------------------------------------------------------------------------------------\n");
					numberOfScriptsThatFailed++;
					continue;
				}

				try
				{
					string fileContents = string.Empty;
					try
					{
						// Read in the file contents
						fileContents = File.ReadAllText(filePath);
						
						// If we should only run sprocs and functions, make sure this script is one of them
						if (settings.OnlyRunProcedureAndFunctionScripts)
						{
							// If this is not a sproc or function (i.e. it is a changescript)
							if (!IsScriptAProcedureOrFunction(fileContents))
							{
								// Log the details that this script was skipped, then move on to the next script
								numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions++;
								skippedTemp = filePath + ": SKIPPED because it is not a Create/Alter Procedure/Function file.\n";
								scriptsSkipped.Append(skippedTemp);
								output.Append(skippedTemp);
								continue;
							}
						}

						// Run the script against the database
						db.ExecuteNonQuery(fileContents);
					}
					catch (Exception ex)
					{
						// If the error was that the sproc/function to create already exists on the database, try changing the script to an Alter instead of a Create.
						if (GetExceptionMessages(ex).Contains("There is already an object named"))
						{
							// Switch any CREATEs to ALTERs
							fileContents = ChangeCreateScriptToAlterScript(fileContents);

							// Run the script against the database
							db.ExecuteNonQuery(fileContents);
						}
						else
							throw ex;
					}

					output.AppendLine(filePath + ": Success");
				}
				// Catch any Scripts that fail to run successfully
				catch (Exception ex)
				{
					numberOfScriptsThatFailed++;
					failedTemp = "--------------------------------------------------------------------------------------------\n";
					failedTemp += filePath + ": FAILED TO RUN\n" + GetExceptionMessages(ex) + "\n";
					scriptsFailed.Append(failedTemp);
					output.Append(failedTemp += "--------------------------------------------------------------------------------------------\n");

					// If we should copy the scripts that fail to a new folder
					if (settings.CopyFailedScriptsToFailedDirectory)
					{
						// If the scripts should be copied into a Failed directory within its current directory
						if (string.IsNullOrWhiteSpace(settings.FailedScriptsDirectory))
							settings.FailedScriptsDirectory = file.Directory + "\\Failed Scripts";
						
						// Get the path to the Failed directory, and create it if it doesn't exist
						if (!Directory.Exists(settings.FailedScriptsDirectory))
							Directory.CreateDirectory(settings.FailedScriptsDirectory);

						// Copy the script file to the Failed directory
						File.Copy(filePath, settings.FailedScriptsDirectory + "\\" + fileName, true);
					}
				}
			}

			// Display all of the scripts that were skipped on purpose
			if (numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions > 0)
			{
				output.Append("\n=================================================================\n");
				output.Append("Scripts Skipped On Purpose\n");
				output.Append("=================================================================\n");
				output.Append(scriptsSkipped);
			}

			// Display all of the scripts that failed
			if (numberOfScriptsThatFailed > 0)
			{
				output.Append("\n=================================================================\n");
				output.Append("Scripts That Failed\n");
				output.Append("=================================================================\n");
				output.Append(scriptsFailed);
			}

			// Create the condensed summary to display
			output.Append("\n======================= Condensed Run Summary ========================\n");

			// Calculate some stats
			int totalNumberOfScriptsToRun = settings.ScriptsToRun.Count;
			int numberOfScriptsThatSucceeded = (totalNumberOfScriptsToRun - numberOfScriptsThatFailed - numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions);

			// Display how many scripts run successfully.
			output.Append(numberOfScriptsThatSucceeded.ToString() + " / " + totalNumberOfScriptsToRun.ToString() +" scripts ran successfully.\n");

			// If some files were skipped, display how many.
			if (numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions > 0)
				output.Append(numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions.ToString() + " script(s) were SKIPPED because they are not a Create/Alter Procedure/Function script.\n");

			// If some scripts failed, display how many.
			if (numberOfScriptsThatFailed > 0)
				output.Append(numberOfScriptsThatFailed.ToString() + " SCRIPT(S) FAILED TO RUN!\n");

			// If all scripts were not ran successfully, tell user to look at log for details.
			if (numberOfScriptsThatFailed > 0 || numberOfScriptsThatWerePurposelyNotRanBecauseTheyAreNotSprocsOrFunctions > 0)
				output.Append("See above for details.\n");


			// Get how long it took to do all of the operations, and create a nice user-friendly string to show it.
			timerToRunAllScripts.Stop();
			string timeToCompleteOperations = string.Empty;
			if (timerToRunAllScripts.Elapsed.Minutes > 0)
				timeToCompleteOperations += timerToRunAllScripts.Elapsed.Minutes.ToString() + " minute(s) and ";
			if (timerToRunAllScripts.Elapsed.TotalSeconds < 0.1)	// Make sure we display the time properly, even for super fast operations that took a fraction of a second to complete.
				timeToCompleteOperations += (timerToRunAllScripts.Elapsed.TotalSeconds - (timerToRunAllScripts.Elapsed.Minutes * 60)).ToString("#.###") + " seconds.";
			else
				timeToCompleteOperations += (timerToRunAllScripts.Elapsed.TotalSeconds - (timerToRunAllScripts.Elapsed.Minutes * 60)).ToString("#.#") + " seconds.";

			// Append how long the operations took to the output.
			output.AppendLine(string.Format("Time it took to run all sql scripts: {0}", timeToCompleteOperations));

			// Cleanup the timer now that we are done with it.
			elapsedTimer.Dispose();

			return output.ToString();
		}

		/// <summary>
		/// Handles the DoWork event of the _scriptRunnerWorker control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.ComponentModel.DoWorkEventArgs"/> instance containing the event data.</param>
		void _scriptRunnerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			// Run the sprocs and save the output in the Result
			ScriptRunnerWorkerSettings workerSettings = e.Argument as ScriptRunnerWorkerSettings;
			e.Result = ApplyStoredProceduresToDB(workerSettings);
		}

		/// <summary>
		/// Handles the RunWorkerCompleted event of the _scriptRunnerWorker control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.ComponentModel.RunWorkerCompletedEventArgs"/> instance containing the event data.</param>
		void _scriptRunnerWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			// Get the output of the operation.
			string output = (string)e.Result;

			// If an error occurred, display it an exit
			if (e.Error != null)
			{
				string errorMessage = GetExceptionMessages(e.Error);
				string errorTitle = "Error Occurred While Trying To Run Scripts";
				MessageBox.Show(errorMessage, errorTitle);
				output += string.Format("\n{0}:\n{1}", errorTitle, errorMessage);
			}

			// Display the output and scroll to the bottom of the output
			txtOutput.Text = output.Trim();
			txtOutput.ScrollToEnd();

			// Re-enable the controls now that the scripts are done running
			EnableControlsAfterRunningScripts();
		}

		/// <summary>
		/// Disables the controls before running the scripts.
		/// </summary>
		private void DisableControlsWhileRunningScripts()
		{
			btnRunScripts.IsEnabled = false;
			txtOutput.IsEnabled = false;
			txtOutput.Text = "Please wait while we run your SQL scripts.....this may take a few minutes depending on how many are being ran...";
		}

		/// <summary>
		/// Enables the controls after the scripts have ran.
		/// </summary>
		private void EnableControlsAfterRunningScripts()
		{
			btnRunScripts.IsEnabled = true;
			txtOutput.IsEnabled = true;
		}

		/// <summary>
		/// Returns true if the script contains CREATE/ALTER PROCEDURE/FUNCTION, false if not.
		/// </summary>
		/// <param name="script">The script to check.</param>
		private bool IsScriptAProcedureOrFunction(string script)
		{
			// Create the regular expressions to match against
			Regex matchCreateProcedure = new Regex(@"create procedure", RegexOptions.IgnoreCase);
			Regex matchCreateFunction = new Regex(@"create function", RegexOptions.IgnoreCase);
			Regex matchAlterProcedure = new Regex(@"alter procedure", RegexOptions.IgnoreCase);
			Regex matchAlterFunction = new Regex(@"alter function", RegexOptions.IgnoreCase);

			// Return if the script contains a Create/Alter Procedure/Function or not.
			return (matchCreateProcedure.IsMatch(script) || matchCreateFunction.IsMatch(script) || matchAlterProcedure.IsMatch(script) || matchAlterFunction.IsMatch(script));
		}

		/// <summary>
		/// Replaces any instances of CREATE PROCEDURE/FUNCTION with ALTER PROCEDURE/FUNCTION and returns the new script.
		/// </summary>
		/// <param name="script">The script to change from create to alter.</param>
		private string ChangeCreateScriptToAlterScript(string script)
		{
			// Switch any CREATEs to ALTERs and return the new script
			Regex matchCreateProcedure = new Regex(@"create procedure", RegexOptions.IgnoreCase);
			Regex matchCreateFunction = new Regex(@"create function", RegexOptions.IgnoreCase);
			script = matchCreateProcedure.Replace(script, "AlTER PROCEDURE");
			script = matchCreateFunction.Replace(script, "AlTER FUNCTION");
			return script;
		}

		/// <summary>
		/// Connects to the specified server and gets the list of valid database names.
		/// </summary>
		/// <param name="forceConnect">If true, no more requests will be made until this one completes, and any resulting error messages will be displayed.</param>
		private void GetDatabaseNames(bool forceConnect = false)
		{
			// Backup the currently selected Database Name
			if (!string.IsNullOrWhiteSpace(cboDatabaseNames.Text))
				_lastDatabaseName = cboDatabaseNames.Text;

			// If we are trying to make a request to the same address we made a request to last time AND this isn't a forced request, or we are already waiting on a forced request, just exit.
			if ((Settings.ServerIP == _lastServerIP && !forceConnect) || _forcedConnectToRetrieveDatabaseNames)
				return;

			// Wipe out the existing database names
			DatabaseNames.Clear();

			// If no server is specified AND this isn't a forced request, just exit
			if (string.IsNullOrWhiteSpace(Settings.ServerIP) && !forceConnect)
				return;
			// Else record the Server we are making the request to
			else
				_lastServerIP = Settings.ServerIP;

			// If we are already trying to connect to the server, cancel it and set up a new worker to handle the new request.
			if (_databaseNameWorker.IsBusy)
			{
				_databaseNameWorker.CancelAsync();
				SetupNewDatabaseNameWorker();
			}

			// If this is a forced connection request, record it.
			if (forceConnect)
				_forcedConnectToRetrieveDatabaseNames = true;

			// Disable controls while getting the Database Names
			DisableControlsBeforeDatabaseNamesConnect();

			// Get the settings to pass to the worker.
			var workerSettings = GetScriptRunnerWorkerSettings();
			workerSettings.ForceConnect = forceConnect;

			// Connect to the server and retrieve the database names
			_databaseNameWorker.RunWorkerAsync(workerSettings);
		}

		/// <summary>
		/// Handles the DoWork event of the _databaseNameWorker control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.ComponentModel.DoWorkEventArgs"/> instance containing the event data.</param>
		void _databaseNameWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			// If this operation has been cancelled
			if (e.Cancel)
				return;

			// Connect to the server to get the list of Database Names
			ScriptRunnerWorkerSettings settings = e.Argument as ScriptRunnerWorkerSettings;
			var serverConnection = new ServerConnection();
			serverConnection.ConnectionString = string.Format("Data Source={0};Integrated Security={1};User ID={2};Password={3};Connect Timeout=30;",
				settings.ServerIP, settings.UseIntegreatedSecurity ? "true" : "false", settings.Username, settings.Password);

			var server = new Server(serverConnection);

			// If this operation has been cancelled
			if (e.Cancel)
				return;

			try
			{
				// Try and connect to the server.
				server.ConnectionContext.Connect();
			}
			// Catch all connection errors.
			catch (Exception ex)
			{
				// If this was a forceful connection attempt, rethrow the error so that it gets displayed to the user in the RunWorkerCompleted function.
				if (settings.ForceConnect)
					throw;
				
				// Else let's just eat the exception and exit. NOM NOM NOM.
				return;
			}

			// If this operation has been cancelled
			if (e.Cancel)
				return;

			// Loop through all of the databases and add their names to the list of database names
			ObservableCollection<string> databaseNames = new ObservableCollection<string>();
			foreach (Database db in server.Databases)
				databaseNames.Add(db.Name);

			server.ConnectionContext.Disconnect();

			// If this operation has been cancelled
			if (e.Cancel)
				return;
			
			e.Result = databaseNames;
		}

		/// <summary>
		/// Handles the RunWorkerCompleted event of the _databaseNameWorker control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.ComponentModel.RunWorkerCompletedEventArgs"/> instance containing the event data.</param>
		void _databaseNameWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			// If this operation was cancelled, just exit, as another request was made and will hit this function.
			if (e.Cancelled)
				return;
			
			// If there was an error
			if (e.Error != null)
			{
				// If this was a forced connection request, show the error message.
				if (_forcedConnectToRetrieveDatabaseNames)
					MessageBox.Show(GetExceptionMessages(e.Error), "Error Retrieving Database Names");

				// Re-enable the Force Connect button now that the worker is done
				EnableControlsAfterDatabaseNamesConnect();

				return;
			}

			// Re-enable the Force Connect button now that the worker is done
			EnableControlsAfterDatabaseNamesConnect();

			// If some database names were returned, use them.
			if (e.Result != null)
				DatabaseNames = e.Result as ObservableCollection<string>;

			// Update the combobox contents
			NotifyThatAllPropertiesWereChanged();

			// We have to update the selected combobox item from a dispatcher to give the app time to first update the comboboxes item source.
			this.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, (System.Threading.ThreadStart)delegate()
			{
				// Select the previously selected Database Name if it is in the new list
				if (cboDatabaseNames.Items.Contains(_lastDatabaseName))
					cboDatabaseNames.SelectedIndex = cboDatabaseNames.Items.IndexOf(_lastDatabaseName);
			});
		}

		/// <summary>
		/// Disables the appropriate controls while loading the Database Names.
		/// </summary>
		private void DisableControlsBeforeDatabaseNamesConnect()
		{
			cboDatabaseNames.IsEnabled = false;
			btnRunScripts.IsEnabled = false;
		}

		/// <summary>
		/// Re-enables the controls after the Database Names request returns.
		/// </summary>
		private void EnableControlsAfterDatabaseNamesConnect()
		{
			cboDatabaseNames.IsEnabled = true;
			btnRunScripts.IsEnabled = true;

			// Enable the forced connection controls as well
			EnableControlsAfterDatabaseNamesConnectForced();
		}

		/// <summary>
		/// Disables the appropriate controls when forcing a connection to get the Database Names.
		/// </summary>
		private void DisableControlsBeforeDatabaseNamesConnectForced()
		{
			btnForceConnect.IsEnabled = false;
			cboServerIPs.IsEnabled = false;
			btnRemoveServerIP.IsEnabled = false;
		}

		/// <summary>
		/// Re=enable the controls after the forced Database Names request returns.
		/// </summary>
		private void EnableControlsAfterDatabaseNamesConnectForced()
		{
			_forcedConnectToRetrieveDatabaseNames = false;

			btnForceConnect.IsEnabled = true;
			cboServerIPs.IsEnabled = true;
			btnRemoveServerIP.IsEnabled = true;
		}

		/// <summary>
		/// Exits the application.
		/// </summary>
		private void ExitApplication()
		{
			Application.Current.Shutdown();
		}

		/// <summary>
		/// Refreshes the list of scripts shown in the listbox
		/// </summary>
		private void RefreshScriptsList()
		{
			// Clear the list of scripts to run
			listScriptsToRun.Items.Clear();

			// Save the Directory to use when running
			CopyComboBoxTextToSettings();

			// If no directory is specified or it doesn't exist, just exit
			if (string.IsNullOrWhiteSpace(Settings.ScriptDirectory) || !Directory.Exists(Settings.ScriptDirectory))
				return;

			// Add the directories scripts to the List Box.
			AddDirectoriesScriptsToListBox(Settings.ScriptDirectory);
		}

		/// <summary>
		/// Adds the directories scripts to the Scripts To Run ListBox.
		/// </summary>
		/// <param name="directoryPath">The directory path.</param>
		private void AddDirectoriesScriptsToListBox(string directoryPath)
		{
			// Add all of the script files in the directory to the list
			DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
			var searchOption = Settings.IncludeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			foreach (FileInfo file in directoryInfo.GetFiles("*.sql", searchOption).Where(f => f.FullName.EndsWith(".sql", StringComparison.InvariantCultureIgnoreCase)))
			{
				listScriptsToRun.Items.Add(file);
			}
		}

		/// <summary>
		/// Handles the Click event of the btnExit control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnExit_Click(object sender, RoutedEventArgs e)
		{
			ExitApplication();
		}

		/// <summary>
		/// Handles the Click event of the btnSaveAndExit control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnSaveAndExit_Click(object sender, RoutedEventArgs e)
		{
			SaveSettings();
			ExitApplication();
		}

		/// <summary>
		/// Handles the Click event of the btnBrowse control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnBrowseForScriptDirectory_Click(object sender, RoutedEventArgs e)
		{
			// Setup the prompt
			System.Windows.Forms.FolderBrowserDialog folderDialog = new System.Windows.Forms.FolderBrowserDialog();
			folderDialog.Description = "Select the folder containing the SQL Scripts (e.g. stored procedures, changescripts) to run...";
			folderDialog.ShowNewFolderButton = true;

			// If the user selected a folder, add it to the combo box, refresh the scripts list, and move focus to it.
			if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				cboScriptDirectories.Text = folderDialog.SelectedPath;
				RefreshScriptsList();
				cboScriptDirectories.Focus();
			}
		}

		/// <summary>
		/// Handles the Click event of the btnBrowseForFailedScriptsDirectory control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnBrowseForFailedScriptsDirectory_Click(object sender, RoutedEventArgs e)
		{
			// Setup the prompt
			System.Windows.Forms.FolderBrowserDialog folderDialog = new System.Windows.Forms.FolderBrowserDialog();
			folderDialog.Description = "Select a folder to copy scripts that fail to run successfully to...";
			folderDialog.ShowNewFolderButton = true;

			// If the user selected a folder, show the path in the text box and move focus to it.
			if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
			{
				txtFailedScriptsDirectory.Text = folderDialog.SelectedPath;
				txtFailedScriptsDirectory.Focus();
			}
		}

		/// <summary>
		/// Handles the Click event of the btnRemoveSelectedScripts control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnRemoveSelectedScripts_Click(object sender, RoutedEventArgs e)
		{
			RemoveSelectedScriptsFromList();
		}

		/// <summary>
		/// Handles the Click event of the btnRefreshScriptsList control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnRefreshScriptsList_Click(object sender, RoutedEventArgs e)
		{
			RefreshScriptsList();
		}

		/// <summary>
		/// Copies the combo box text values to the Settings.
		/// </summary>
		private void CopyComboBoxTextToSettings()
		{
			Settings.DatabaseName = cboDatabaseNames.Text;
			Settings.ServerIP = cboServerIPs.Text.Trim();
			Settings.ScriptDirectory = cboScriptDirectories.Text.Trim();
			Settings.NotifyThatAllPropertiesWereChanged();
		}

		/// <summary>
		/// Returns a string containing the exceptions error message, as well as all inner exception error messages.
		/// </summary>
		/// <param name="ex">The exception whose error message should be returned</param>
		private string GetExceptionMessages(Exception ex)
		{
			// If an invalid exception was given, just return an empty string
			if (ex == null) return string.Empty;

			// Get the original message
			string errorMessage = ex.Message;

			// Loop through and append all of the inner exception messages
			Exception innerEx = ex.InnerException;
			while (innerEx != null)
			{
				errorMessage += "\nInner Exception: " + innerEx.Message;
				innerEx = innerEx.InnerException;
			}

			return errorMessage;
		}

		/// <summary>
		/// Handles the KeyDown event of the cboServerIPs control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void cboServerIPs_KeyDown(object sender, KeyEventArgs e)
		{
			// If Enter was pressed, add the current Text as an item to the list if it's not already in there.
			if (e.Key == Key.Enter || e.Key == Key.Return)
			{
				// Get the specified Server IP
				string serverIP = cboServerIPs.Text.Trim();

				// If no IP is specified, just exit
				if (string.IsNullOrWhiteSpace(serverIP))
					return;

				// Add the Server IP to the list
				if (!Settings.ServerIPs.Contains(serverIP))
					Settings.ServerIPs.Add(serverIP);

				// Update bindings
				NotifyThatAllPropertiesWereChanged();
			}
		}

		/// <summary>
		/// Handles the KeyUp event of the cboServerIPs control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void cboServerIPs_KeyUp(object sender, KeyEventArgs e)
		{
			CopyComboBoxTextToSettings();
			GetDatabaseNames();
		}

		/// <summary>
		/// Handles the DropDownClosed event of the cboServerIPs control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
		private void cboServerIPs_DropDownClosed(object sender, EventArgs e)
		{
			CopyComboBoxTextToSettings();
			GetDatabaseNames();
		}

		/// <summary>
		/// Handles the Click event of the btnForceConnect control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnForceConnect_Click(object sender, RoutedEventArgs e)
		{
			// Disable this button until the worker finishes
			btnForceConnect.IsEnabled = false;

			// Disable the Forced Connection controls while we make the request
			DisableControlsBeforeDatabaseNamesConnectForced();

			// Get the Database Names and mark that this is a forced connection so that we see any connection errors
			GetDatabaseNames(true);
		}

		/// <summary>
		/// Handles the KeyUp event of the cboDatabaseNames control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void cboDatabaseNames_KeyUp(object sender, KeyEventArgs e)
		{
			CopyComboBoxTextToSettings();
		}

		/// <summary>
		/// Handles the DropDownClosed event of the cboDatabaseNames control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
		private void cboDatabaseNames_DropDownClosed(object sender, EventArgs e)
		{
			CopyComboBoxTextToSettings();
		}

		/// <summary>
		/// Handles the KeyDown event of the cboScriptDirectories control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void cboScriptDirectories_KeyDown(object sender, KeyEventArgs e)
		{
			// If Enter was pressed, add the current Text as an item to the list if it's not already in there.
			if (e.Key == Key.Enter || e.Key == Key.Return)
			{
				// Get the specified Directory
				string directory = cboScriptDirectories.Text.Trim();

				// If no Directory is specified, just exit
				if (string.IsNullOrWhiteSpace(directory))
					return;

				// If the specified Directory doesn't exist, show an error message and exit.
				if (!Directory.Exists(directory))
				{
					MessageBox.Show("The folder \"" + directory + "\" does not exist.", "Folder Not Added");
					return;
				}

				// If the Directory is not already in our list, add it
				if (!Settings.ScriptDirectories.Contains(directory))
					Settings.ScriptDirectories.Add(directory);

				// Update bindings
				NotifyThatAllPropertiesWereChanged();
			}

			CopyComboBoxTextToSettings();
		}

		/// <summary>
		/// Handles the KeyUp event of the cboScriptDirectories control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void cboScriptDirectories_KeyUp(object sender, KeyEventArgs e)
		{
			// Refresh the Scripts list to show the scripts in the directory
			RefreshScriptsList();
		}

		/// <summary>
		/// Handles the DropDownClosed event of the cboScriptDirectories control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
		private void cboScriptDirectories_DropDownClosed(object sender, EventArgs e)
		{
			RefreshScriptsList();
		}

		/// <summary>
		/// Handles the Click event of the btnRemoveServerIP control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnRemoveServerIP_Click(object sender, RoutedEventArgs e)
		{
			// Get the specified Server IP
			string serverIP = cboServerIPs.Text.Trim();

			// If no IP is specified, just exit
			if (string.IsNullOrWhiteSpace(serverIP))
				return;

			// Remove the Server IP from the list
			if (Settings.ServerIPs.Contains(serverIP))
				Settings.ServerIPs.Remove(serverIP);

			// Update bindings
			NotifyThatAllPropertiesWereChanged();
		}

		/// <summary>
		/// Handles the Click event of the btnRemoveScriptDirectory control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data.</param>
		private void btnRemoveScriptDirectory_Click(object sender, RoutedEventArgs e)
		{
			// Get the specified Directory
			string directory = cboScriptDirectories.Text.Trim();

			// If no Directory is specified, just exit
			if (string.IsNullOrWhiteSpace(directory))
				return;

			// Remove the Directory from the list
			if (Settings.ScriptDirectories.Contains(directory))
				Settings.ScriptDirectories.Remove(directory);

			// Update bindings
			NotifyThatAllPropertiesWereChanged();
		}

		/// <summary>
		/// Handles the KeyDown event of the listScriptsToRun control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="System.Windows.Input.KeyEventArgs"/> instance containing the event data.</param>
		private void listScriptsToRun_KeyDown(object sender, KeyEventArgs e)
		{
			// If the user pressed the Delete key, remove any selected scripts from the list of scripts to run.
			if (e.Key == Key.Delete)
				RemoveSelectedScriptsFromList();
		}

		/// <summary>
		/// Removes the selected scripts from the list of scripts to run.
		/// </summary>
		private void RemoveSelectedScriptsFromList()
		{
			List<FileInfo> filesToRemove = new List<FileInfo>();

			// Copy the selected strings into a temp list
			foreach (FileInfo item in listScriptsToRun.SelectedItems)
				filesToRemove.Add(item);

			int highestIndexOfRemovedItems = 0;

			// Remove all of the selected scripts from the listbox
			foreach (FileInfo item in filesToRemove)
			{
				// Keep track of the position of the last item removed
				highestIndexOfRemovedItems = Math.Max(highestIndexOfRemovedItems, listScriptsToRun.Items.IndexOf(item));
				listScriptsToRun.Items.Remove(item);
			}

			// Select the next item in the list, since we've removed all of the previously selected ones
			listScriptsToRun.SelectedIndex = (highestIndexOfRemovedItems >= listScriptsToRun.Items.Count) ? listScriptsToRun.Items.Count - 1 : highestIndexOfRemovedItems;
		}

		/// <summary>
		/// Handles the Drop event of the listScriptsToRun control.
		/// Adds the selected files and folders dropped onto the list of scripts to run. For dropped
		/// folders, all files contained within the folder are added (sub-folders are ignored).
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="DragEventArgs"/> instance containing the event data.</param>
		private void listScriptsToRun_Drop(object sender, DragEventArgs e)
		{
			var paths = (string[])e.Data.GetData(DataFormats.FileDrop, false);
			foreach (string path in paths)
			{
				// If this is a directory, add any script files within the directory.
				if (Directory.Exists(path))
				{
					AddDirectoriesScriptsToListBox(path);
				}
				// Else this is a file, so add it if it is a script file.
				else if (path.EndsWith(".sql", StringComparison.InvariantCultureIgnoreCase))
				{
					listScriptsToRun.Items.Add(new FileInfo(path));
				}
			}
		}

		/// <summary>
		/// Handles the CheckedChanged event of the chkIncludeSubDirectories control.
		/// </summary>
		/// <param name="sender">The source of the event.</param>
		/// <param name="e">The <see cref="RoutedEventArgs"/> instance containing the event data.</param>
		private void chkIncludeSubDirectories_CheckedChanged(object sender, RoutedEventArgs e)
		{
			// Refresh the list of scripts now that the Include Sub Directories options was toggled.
			RefreshScriptsList();
		}
	}
}
