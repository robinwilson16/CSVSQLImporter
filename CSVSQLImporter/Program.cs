using CSVSQLImporter.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using WinSCP;

namespace CSVSQLImporter
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            bool? logToFile = true;
            bool? outputToScreen = true;

            string? toolName = Assembly.GetExecutingAssembly().GetName().Name;
            string logFileName = $"{toolName} - {DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")}.log";

            await LoggingService.Log(toolName, logFileName, logToFile, outputToScreen);
            await LoggingService.Log($"Import CSV File to SQL Table", logFileName, logToFile, outputToScreen);
            await LoggingService.Log($"=========================================", logFileName, logToFile, outputToScreen);

            string? productVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            await LoggingService.Log($"\nVersion {productVersion}", logFileName, logToFile, outputToScreen);
            await LoggingService.Log($"\nCopyright Robin Wilson", logFileName, logToFile, outputToScreen);

            string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            string? customConfigFile = null;
            if (args.Length >= 1)
            {
                customConfigFile = args[0];
            }

            if (!string.IsNullOrEmpty(customConfigFile))
            {
                configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, customConfigFile);
            }

            await LoggingService.Log($"\nUsing Config File {configFile}", logFileName, logToFile, outputToScreen);

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(configFile, optional: false);

            IConfiguration config;
            try
            {
                config = builder.Build();
            }
            catch (Exception e)
            {
                await LoggingService.Log($"Error: {e}", logFileName, logToFile, outputToScreen);
                return 1;
            }

            await LoggingService.Log($"\nSetting Locale To {config["Locale"]}", logFileName, logToFile, outputToScreen);

            //Set locale to ensure dates and currency are correct
            CultureInfo culture = new CultureInfo(config["Locale"] ?? "en-GB");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var databaseConnection = config.GetSection("DatabaseConnection");
            var databaseTable = config.GetSection("DatabaseTable");
            string? schemaName = databaseTable["Schema"] ?? "dbo";
            var csvFile = config.GetSection("CSVFile");
            var ftpConnection = config.GetSection("FTPConnection");
            var storedProcedure = config.GetSection("StoredProcedure");
            string[]? filePaths = { @csvFile["Folder"] ?? "", csvFile["FileName"] ?? "" };
            string csvFilePath = Path.Combine(filePaths);
            string? csvFileNameNoExtension = csvFile["FileName"]?.Substring(0, csvFile["FileName"]!.LastIndexOf("."));
            

            var sqlConnection = new SqlConnectionStringBuilder
            {
                DataSource = databaseConnection["Server"],
                UserID = databaseConnection["Username"],
                Password = databaseConnection["Password"],
                IntegratedSecurity = databaseConnection.GetValue<bool>("UseWindowsAuth", false),
                InitialCatalog = databaseConnection["Database"],
                TrustServerCertificate = true
            };

            //If not using windows auth then need username and password values too
            if (sqlConnection.IntegratedSecurity == false)
            {
                sqlConnection.UserID = databaseConnection["Username"];
                sqlConnection.Password = databaseConnection["Password"];
            }

            var connectionString = sqlConnection.ConnectionString;

            //Get CSV File

            if (ftpConnection.GetValue<bool?>("DownloadFile", false) == true)
            {
                // Setup session options
                SessionOptions sessionOptions = new SessionOptions
                {
                    HostName = ftpConnection["Server"],
                    PortNumber = ftpConnection.GetValue<int>("Port", 21),
                    UserName = ftpConnection["Username"],
                    Password = ftpConnection["Password"]
                };

                switch (ftpConnection?["Type"])
                {
                    case "FTP":
                        sessionOptions.Protocol = Protocol.Ftp;
                        break;
                    case "FTPS":
                        sessionOptions.Protocol = Protocol.Ftp;
                        sessionOptions.FtpSecure = FtpSecure.Explicit;
                        sessionOptions.GiveUpSecurityAndAcceptAnyTlsHostCertificate = true;
                        break;
                    case "SFTP":
                        sessionOptions.Protocol = Protocol.Sftp;
                        sessionOptions.GiveUpSecurityAndAcceptAnyTlsHostCertificate = true;
                        break;
                    case "SCP":
                        sessionOptions.Protocol = Protocol.Scp;
                        sessionOptions.GiveUpSecurityAndAcceptAnyTlsHostCertificate = true;
                        break;
                    default:
                        sessionOptions.Protocol = Protocol.Ftp;
                        break;
                }

                if (ftpConnection?["SSHHostKeyFingerprint"]?.Length > 0)
                {
                    sessionOptions.SshHostKeyFingerprint = ftpConnection["SSHHostKeyFingerprint"];
                    sessionOptions.GiveUpSecurityAndAcceptAnyTlsHostCertificate = false;
                }

                switch (ftpConnection?["Mode"])
                {
                    case "Active":
                        sessionOptions.FtpMode = FtpMode.Active;
                        break;
                    case "Passive":
                        sessionOptions.FtpMode = FtpMode.Passive;
                        break;
                    default:
                        sessionOptions.FtpMode = FtpMode.Passive;
                        break;
                }

                await LoggingService.Log("\nDownloading CSV File", logFileName, logToFile, outputToScreen);
                await LoggingService.Log($"Downloding File {csvFile["FileName"]} From {sessionOptions.HostName}", logFileName, logToFile, outputToScreen);

                try
                {
                    using (Session session = new Session())
                    {
                        //When publishing to a self-contained exe file need to specify the location of WinSCP.exe
                        session.ExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WinSCP.exe");

                        // Connect
                        session.Open(sessionOptions);

                        // Download files
                        string downloadpath = Path.Combine("/", ftpConnection?["FolderPath"] ?? "");

                        if (downloadpath.Substring(downloadpath.Length - 1) != "/")
                        {
                            downloadpath = downloadpath + "/";
                        }

                        TransferOptions transferOptions = new TransferOptions();
                        transferOptions.TransferMode = TransferMode.Binary;

                        TransferOperationResult transferResult;
                        transferResult =
                            session.GetFiles(downloadpath + csvFile["FileName"], @csvFilePath, false, transferOptions);

                        // Throw on any error
                        transferResult.Check();

                        // Print results
                        foreach (TransferEventArgs transfer in transferResult.Transfers)
                        {
                            await LoggingService.Log($"Download of {transfer.FileName} succeeded from {downloadpath}", logFileName, logToFile, outputToScreen);
                        }
                    }
                }
                catch (Exception e)
                {
                    await LoggingService.Log($"Error: {e}", logFileName, logToFile, outputToScreen);
                    return 1;
                }
            }
            else
            {
                await LoggingService.Log($"Not Downloading File to FTP as Option in Config is False", logFileName, logToFile, outputToScreen);
            }

            //Load CSV File
            await LoggingService.Log("\nLoading Data from CSV", logFileName, logToFile, outputToScreen);
            await LoggingService.Log($"Loading CSV File from {csvFilePath}", logFileName, logToFile, outputToScreen);

            DataTable table;

            string? tableNameOverride = null;

            if (databaseTable?["TableNameOverride"]?.Length > 0)
            {
                tableNameOverride = databaseTable?["TableNameOverride"];
            }

            if (System.IO.File.Exists(csvFilePath))
            {
                table = new DataTable(databaseTable?["TablePrefix"] + (tableNameOverride ?? csvFileNameNoExtension));

                table.Rows.Clear();
                table.Columns.Clear();

                DataRow tableRow;


                List<string> fileRows = new List<string>();

                //Need to read entire file first as cannot just use reader.ReadLine() and Split() as data may contain commas
                List< List<string> > csvData = new List<List<string>>();
                using (var reader = new StreamReader(@csvFilePath))
                {
                    var allLines = await reader.ReadToEndAsync();

                    if (allLines != null)
                    {
                        csvData = ParseCsv(allLines, csvFile.GetValue<char>("Delimiter", ','));
                    }
                }

                await LoggingService.Log($"Loaded {csvData.Count} rows of data from {csvFilePath}", logFileName, logToFile, outputToScreen);

                //Testing outputs
                //int rowIndexTest = 0;
                //foreach (List<string> fileRow in csvData)
                //{
                //    rowIndexTest++;

                //    //fieldValues = fileRow?.Split(csvFile["Delimiter"]) ?? Array.Empty<string>();

                //    await LoggingService.Log($"Row {rowIndexTest} - {fileRow[2]}");
                //}

                int rowIndex = 0;
                //Process rows and place into data table with named columns, correct data types
                foreach (List<string> fileRow in csvData)
                {
                    int colIndex = 0;

                    if (fileRow.Count > 0)
                    {
                        //If first row then add named columns and don't add a row
                        if (rowIndex == 0)
                        {
                            foreach (string fieldHeaderValue in fileRow)
                            {
                                string? fieldRowValue = null;
                                string? fieldTypeToUse = "";
                                List<string> fieldTypes = new List<string>();

                                //Check all cell types for this column excluding header row to pick best type
                                for (int i = 0; i < csvData.Count - 1; i++)
                                {
                                    fieldRowValue = csvData[i + 1][colIndex];
                                    string fieldType = "System.String";
                                    /*
                                    switch (Type.GetTypeCode(fieldRowValue.GetType()))
                                    {
                                        case TypeCode.Boolean: fieldTypes[i] = "System.Boolean"; break;
                                        case TypeCode.String: fieldTypes[i] = "System.String"; break;
                                        case TypeCode.Decimal: fieldTypes[i] = "System.Double"; break;
                                        case TypeCode.Int16: fieldTypes[i] = "System.Int16"; break;
                                        case TypeCode.Int32: fieldTypes[i] = "System.Int32"; break;
                                        case TypeCode.Int64: fieldTypes[i] = "System.Int64"; break;
                                        case TypeCode.DateTime: fieldTypes[i] = "System.DateTime"; break;
                                        default: fieldTypes[i] = "System.String"; break;
                                    }
                                    */
                                    //Skip Null/blank fields as cannot determine a type from these
                                    //If all rows are null in this column then will default to string
                                    if (fieldRowValue.Length > 0)
                                    {
                                        if (bool.TryParse(fieldRowValue, out bool fieldRowValueBoolean))
                                        {
                                            fieldType = "System.Boolean";
                                        }
                                        else if (int.TryParse(fieldRowValue, out int fieldRowValueInt))
                                        {
                                            fieldType = "System.Int32";
                                        }
                                        // Additional code needed so values such as "4+" are not treated as doubles/decimals
                                        else if (decimal.TryParse(fieldRowValue, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out decimal fieldRowValueDecimal))
                                        {
                                            fieldType = "System.Double";
                                        }
                                        else if (DateTime.TryParse(fieldRowValue, out DateTime fieldRowValueDateTime))
                                        {
                                            fieldType = "System.DateTime";
                                        }
                                        else
                                        {
                                            fieldType = "System.String";
                                        }

                                        fieldTypes.Add(fieldType);
                                    }

                                    //Output types to check for specific column
                                    //if (colIndex == 35)
                                    //{
                                    //    await LoggingService.Log($"Value '{fieldRowValue}' is {fieldType}", logFileName, logToFile, outputToScreen);
                                    //}
                                }

                                //Pick best type of field
                                if (fieldTypes.Contains("System.String"))
                                {
                                    //If any of the rows contain a string then need to set row to that to be able to store all values
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Double") && fieldTypes.Contains("System.DateTime"))
                                {
                                    //If rows are mixed types then store as string
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Int32") && fieldTypes.Contains("System.DateTime"))
                                {
                                    //If rows are mixed types then store as string
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Boolean") && fieldTypes.Contains("System.DateTime"))
                                {
                                    //If rows are mixed types then store as string
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Double") && fieldTypes.Contains("System.String"))
                                {
                                    //If rows are mixed types then store as string
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Int32") && fieldTypes.Contains("System.String"))
                                {
                                    //If rows are mixed types then store as string
                                    fieldTypeToUse = "System.String";
                                }
                                else if (fieldTypes.Contains("System.Double")) 
                                {
                                    fieldTypeToUse = "System.Double";
                                }
                                else if (fieldTypes.Contains("System.Int32"))
                                {
                                    fieldTypeToUse = "System.Int32";
                                }
                                else if (fieldTypes.Contains("System.Boolean"))
                                {
                                    fieldTypeToUse = "System.Boolean";
                                }
                                else if (fieldTypes.Contains("System.DateTime"))
                                {
                                    fieldTypeToUse = "System.DateTime";
                                }
                                else
                                {
                                    fieldTypeToUse = "System.String";
                                }

                                //Get the name of the column
                                string? colName = "Column_{0}";
                                if (csvFile.GetValue<bool?>("ContainsHeaders", false) == true)
                                {
                                    try { colName = fieldHeaderValue?.ToString()?.Trim('"'); }
                                    catch { colName = string.Format(colName ?? "", colIndex); }
                                }
                                else
                                {
                                    colName = string.Format(colName ?? "", colIndex);

                                    //add values for fields in row then add row
                                    tableRow = table!.NewRow();

                                    foreach (object? fieldValue in fileRow)
                                    {
                                        object? fieldVal = fieldValue?.ToString()?.Trim('"');
                                        //If the cell has a blank value then make it null in the SQL Table
                                        if (fieldVal?.ToString()?.Length == 0)
                                        {
                                            fieldVal = DBNull.Value;
                                        }

                                        //Add the cell to the row
                                        if (tableRow != null)
                                        {
                                            if (colIndex <= table?.Columns.Count - 1) tableRow[colIndex] = fieldVal;
                                        }

                                        colIndex++;
                                    }

                                    //Add the row to the table
                                    if (tableRow != null && table != null)
                                    {
                                        if (rowIndex > 0) table.Rows.Add(tableRow);
                                    }
                                }
                                

                                //await LoggingService.Log($"\n{fieldHeaderValue}");

                                //Add field to the table
                                DataColumn tableColumn = new DataColumn(colName, Type.GetType(fieldTypeToUse) ?? typeof(string));
                                table?.Columns.Add(tableColumn);
                                colIndex++;
                            }
                        }
                        else
                        {
                            //add values for fields in row then add row
                            tableRow = table!.NewRow();

                            foreach (object? fieldValue in fileRow)
                            {
                                object? fieldVal = fieldValue?.ToString()?.Trim('"');
                                //If the cell has a blank value then make it null in the SQL Table
                                if (fieldVal?.ToString()?.Length == 0)
                                {
                                    fieldVal = DBNull.Value;
                                }

                                //Add the cell to the row
                                if (tableRow != null)
                                {
                                    if (colIndex <= table?.Columns.Count - 1) tableRow[colIndex] = fieldVal;
                                }

                                colIndex++;
                            }

                            //Add the row to the table
                            if (tableRow != null && table != null)
                            {
                                if (rowIndex > 0) table.Rows.Add(tableRow);
                            }
                        }
                    }

                    rowIndex++;
                }

                table?.AcceptChanges();

                //Testing outputs
                //await LoggingService.Log($"\nValue {table?.Rows[0][1]} {table?.Rows[0][1].GetType()}");
            }
            else
            {
                await LoggingService.Log($"The File at {csvFilePath} Could Not Be Found", logFileName, logToFile, outputToScreen);
                return 1;
            }

            await LoggingService.Log($"Loaded {table?.Rows.Count} rows of data from file", logFileName, logToFile, outputToScreen);

            //Save to Database
            await LoggingService.Log("\nSaving Data To Database", logFileName, logToFile, outputToScreen);
            await LoggingService.Log($"Creating Table {table?.TableName} in Database", logFileName, logToFile, outputToScreen);
            await using var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();

                if (table != null)
                {
                    string? createTableSQL = CreateTableSQL(schemaName ?? "dbo", table?.TableName ?? "Imported_CSV_File", table!);
                    //await LoggingService.Log($"{createTableSQL}");

                    using (SqlCommand command = new SqlCommand(createTableSQL, connection))
                        await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception e)
            {
                await LoggingService.Log(e.ToString(), logFileName, logToFile, outputToScreen);

                if (connection != null)
                {
                    await connection.CloseAsync();
                }

                return 1;
            }

            await LoggingService.Log($"Uploading {table?.Rows.Count} Rows of Data into Table {table?.TableName} in Database", logFileName, logToFile, outputToScreen);

            try
            {
                SqlBulkCopy bulkcopy = new SqlBulkCopy(connection);
                bulkcopy.DestinationTableName = table?.TableName;

                await bulkcopy.WriteToServerAsync(table);
            }
            catch (Exception e)
            {
                await LoggingService.Log(e.ToString(), logFileName, logToFile, outputToScreen);

                if (connection != null)
                {
                    await connection.CloseAsync();
                }

                return 1;
            }

            //Run Stored Procedure On Completion
            if (storedProcedure.GetValue<bool?>("RunTask", false) == true)
            {
                await LoggingService.Log("\nRunning Post Import Stored Procedure", logFileName, logToFile, outputToScreen);
                await LoggingService.Log($"Running Stored Procedure: {storedProcedure["Database"]}.{storedProcedure["Schema"]}.{storedProcedure["StoredProcedure"]}", logFileName, logToFile, outputToScreen);

                if (storedProcedure["StoredProcedure"]?.Length > 0)
                {
                    try
                    {
                        if (table != null)
                        {
                            string customTaskSQL = $"EXEC {storedProcedure["Database"]}.{storedProcedure["Schema"]}.{storedProcedure["StoredProcedure"]}";
                            //await LoggingService.Log($"{createTableSQL}");

                            using (SqlCommand command = new SqlCommand(customTaskSQL, connection))
                                await command.ExecuteNonQueryAsync();

                            await LoggingService.Log($"Stored Procedure Completed", logFileName, logToFile, outputToScreen);
                        }
                    }
                    catch (Exception e)
                    {
                        await LoggingService.Log(e.ToString(), logFileName, logToFile, outputToScreen);

                        if (connection != null)
                        {
                            await connection.CloseAsync();
                        }

                        return 1;
                    }
                }
                else
                {
                    await LoggingService.Log($"Cannot run stored procedure as it has not been specified in the config file", logFileName, logToFile, outputToScreen);
                }
            }

            //Close database connection
            if (connection != null)
            {
                await connection.CloseAsync();
            }

            return 0;
        }

        public static string CreateTableSQL(string schemaName, string tableName, DataTable table)
        {
            string sqlsc;
            sqlsc = $"\n DROP TABLE IF EXISTS [{schemaName ?? "dbo"}].[{tableName}];";
            sqlsc += $"\n CREATE TABLE [{schemaName ?? "dbo"}].[{tableName}] (";

            //Check Cell Value
            //await LoggingService.Log(table.Rows[1][4].ToString());

            for (int i = 0; i < table.Columns.Count; i++)
            {
                sqlsc += "\n [" + table.Columns[i].ColumnName + "]";
                System.Type columnType = table.Columns[i].DataType;

                if (columnType == typeof(Int32))
                {
                    sqlsc += " INT";
                }
                else if (columnType == typeof(Int64))
                {
                    sqlsc += " BIGINT";
                }
                else if (columnType == typeof(Int16))
                {
                    sqlsc += " SMALLINT";
                }
                else if (columnType == typeof(Byte))
                {
                    sqlsc += " TINYINT";
                }
                else if (columnType == typeof(System.Decimal))
                {
                    sqlsc += " DECIMAL";
                }
                else if (columnType == typeof(Double))
                {
                    sqlsc += " FLOAT";
                }
                else if (columnType == typeof(DateTime))
                {
                    sqlsc += " DATETIME";
                }
                else if (columnType == typeof(string))
                {
                    int rowLength = 0;
                    int maxRowLength = 0;
                    string maxRowLengthString = "";
                    if (table.Columns[i].MaxLength == -1)
                    {                        
                        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                        {
                            rowLength = (table.Rows[rowIndex][i].ToString() ?? "").Length;
                            //await LoggingService.Log($"Column: {i}, Row: {rowIndex}, Length: {rowLength}");

                            if (rowLength > maxRowLength)
                            {
                                maxRowLength = rowLength;
                            }
                        }
                    }
                    else
                    {
                        maxRowLength = table.Columns[i].MaxLength;
                    }

                    if (maxRowLength > 0 && maxRowLength < 4000)
                    {
                        maxRowLengthString = maxRowLength.ToString();
                    }
                    else
                    {
                        maxRowLengthString = "MAX";
                    }
                    sqlsc += string.Format(" NVARCHAR({0})", maxRowLengthString);
                }
                else
                {
                    int rowLength = 0;
                    int maxRowLength = 0;
                    string maxRowLengthString = "";
                    if (table.Columns[i].MaxLength == -1)
                    {
                        for (int rowIndex = 1; rowIndex < table.Rows.Count; rowIndex++)
                        {
                            rowLength = (table.Rows[rowIndex][i].ToString() ?? "").Length;
                            if (rowLength > maxRowLength)
                            {
                                maxRowLength = rowLength;
                            }
                        }
                    }
                    else
                    {
                        maxRowLength = table.Columns[i].MaxLength;
                    }

                    if (maxRowLength > 0 && maxRowLength < 4000)
                    {
                        maxRowLengthString = maxRowLength.ToString();
                    }
                    else
                    {
                        maxRowLengthString = "MAX";
                    }
                    sqlsc += string.Format(" NVARCHAR({0})", maxRowLengthString);
                }

                if (table.Columns[i].AutoIncrement)
                    sqlsc += " IDENTITY(" + table.Columns[i].AutoIncrementSeed.ToString() + "," + table.Columns[i].AutoIncrementStep.ToString() + ")";
                if (!table.Columns[i].AllowDBNull)
                    sqlsc += " NOT NULL";
                sqlsc += ",";
            }
            return sqlsc.Substring(0, sqlsc.Length - 1) + "\n)";
        }

        static List<List<string>> ParseCsv(string csv, char delimiter)
        {
            var parsedCsv = new List<List<string>>();
            var row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotedField = false;

            //If CSV does not end with a new line character then add one to ensure final line of data is included
            if (csv.Substring(csv.Length - 1, 1) != "\n")
            {
                csv = csv += "\n";
            }

            for (int i = 0; i < csv.Length; i++)
            {
                char current = csv[i];
                char next = i == csv.Length - 1 ? ' ' : csv[i + 1];

                // If current character is not a quote or comma or carriage return or newline (or not a quote and currently in an a quoted field), just add the character to the current field text
                if ((current != '"' && current != delimiter && current != '\r' && current != '\n') || (current != '"' && inQuotedField))
                {
                    field.Append(current);
                }
                // Ignore whitespace outside a quoted field
                else if (current == ' ' || current == '\t')
                {
                    continue; 
                }
                else if (current == '"')
                {
                    if (inQuotedField && next == '"')
                    { // Quote is escaping a quote within a quoted field
                        i++; // Skip escaping quote
                        field.Append(current);
                    }
                    else if (inQuotedField)
                    { // Quote signifies the end of a quoted field
                        row.Add(field.ToString());
                        if (next == delimiter)
                        {
                            // Skip the comma separator since we've already found the end of the field
                            i++; 
                        }
                        field = new StringBuilder(); //Clear value
                        inQuotedField = false;
                    }
                    else
                    { // Quote signifies the beginning of a quoted field
                        inQuotedField = true;
                    }
                }
                else if (current == delimiter)
                { //
                    row.Add(field.ToString());
                    field = new StringBuilder(); //Clear value
                }
                else if (current == '\n')
                {
                    row.Add(field.ToString());
                    parsedCsv.Add(new List<string>(row));
                    field = new StringBuilder(); //Clear value
                    row.Clear();
                }
            }

            return parsedCsv;
        }
    }
}