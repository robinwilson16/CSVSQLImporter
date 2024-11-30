using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic;
using System.Data;
using WinSCP;

namespace CSVSQLImporter
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("\nImport CSV File to SQL Table");
            Console.WriteLine("=========================================\n");
            Console.WriteLine("Copyright Robin Wilson");

            string configFile = "appsettings.json";
            string? customConfigFile = null;
            if (args.Length >= 1)
            {
                customConfigFile = args[0];
            }

            if (!string.IsNullOrEmpty(customConfigFile))
            {
                configFile = customConfigFile;
            }

            Console.WriteLine($"\nUsing Config File {configFile}");

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
                Console.WriteLine("Error: {0}", e);
                return 1;
            }

            var databaseConnection = config.GetSection("DatabaseConnection");
            var databaseTable = config.GetSection("DatabaseTable");
            var csvFile = config.GetSection("CSVFile");
            var ftpConnection = config.GetSection("FTPConnection");
            string csvFilePath = csvFile["Folder"] + "\\" + csvFile["FileName"];
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

                switch (ftpConnection["Type"])
                {
                    case "FTP":
                        sessionOptions.Protocol = Protocol.Ftp;
                        break;
                    case "FTPS":
                        sessionOptions.Protocol = Protocol.Ftp;
                        sessionOptions.FtpSecure = FtpSecure.Explicit;
                        break;
                    case "SFTP":
                        sessionOptions.Protocol = Protocol.Sftp;
                        break;
                    default:
                        sessionOptions.Protocol = Protocol.Ftp;
                        break;
                }

                switch (ftpConnection["Mode"])
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

                Console.WriteLine($"Downloding File {csvFile["FileName"]} From {sessionOptions.HostName}");

                try
                {
                    using (Session session = new Session())
                    {
                        //When publishing to a self-contained exe file need to specify the location of WinSCP.exe
                        session.ExecutablePath = AppDomain.CurrentDomain.BaseDirectory + "\\WinSCP.exe";

                        // Connect
                        session.Open(sessionOptions);

                        // Upload files
                        TransferOptions transferOptions = new TransferOptions();
                        transferOptions.TransferMode = TransferMode.Binary;

                        TransferOperationResult transferResult;
                        transferResult =
                            session.GetFiles("/" + csvFile["FileName"], @csvFilePath, false, transferOptions);

                        // Throw on any error
                        transferResult.Check();

                        // Print results
                        foreach (TransferEventArgs transfer in transferResult.Transfers)
                        {
                            Console.WriteLine("Download of {0} succeeded", transfer.FileName);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error: {0}", e);
                    return 1;
                }
            }
            else
            {
                Console.WriteLine($"Not Uploading File to FTP as Option in Config is False");
            }

            //Load CSV File
            Console.WriteLine($"Loading CSV File from {csvFilePath}");

            DataTable table;
            if (System.IO.File.Exists(csvFilePath))
            {
                table = new DataTable(databaseTable["TablePrefix"] + csvFileNameNoExtension);

                table.Rows.Clear();
                table.Columns.Clear();

                DataRow tableRow;


                List<string> fileRows = new List<string>();

                //Read in file line by line as array of strings
                using (var reader = new StreamReader(@"C:\Reports\Education & Training - Overall by Team.csv"))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();

                        if (line != null)
                        {
                            fileRows.Add(line);
                        }
                    }
                }

                //Process rows and place into data table with named columns, correct data types
                int rowIndex = 0;
                foreach (string fileRow in fileRows)
                {
                    var values = fileRow?.Split(',');

                    int colIndex = 0;

                    if (values?.Length > 0)
                    {
                        if (rowIndex == 0)
                        {
                            //If first row then add named columns and don't add a row
                            foreach (string fieldHeaderValue in values)
                            {
                                string? fieldRowValue = null;
                                string? fieldType = "";
                                string[] fieldType2 = new string[2];

                                //Check values from row 2 and 3 as row 1 is titles so all will be strings
                                for (int i = 0; i < 2; i++)
                                {
                                    fieldRowValue = fileRows[1].Split(',')[colIndex];

                                    /*
                                    switch (Type.GetTypeCode(fieldRowValue.GetType()))
                                    {
                                        case TypeCode.Boolean: fieldType2[i] = "System.Boolean"; break;
                                        case TypeCode.String: fieldType2[i] = "System.String"; break;
                                        case TypeCode.Decimal: fieldType2[i] = "System.Double"; break;
                                        case TypeCode.Int16: fieldType2[i] = "System.Int16"; break;
                                        case TypeCode.Int32: fieldType2[i] = "System.Int32"; break;
                                        case TypeCode.Int64: fieldType2[i] = "System.Int64"; break;
                                        case TypeCode.DateTime: fieldType2[i] = "System.DateTime"; break;
                                        default: fieldType2[i] = "System.String"; break;
                                    }
                                    */
                                    if (bool.TryParse(fieldRowValue, out bool fieldRowValueBoolean))
                                    {
                                        fieldType2[i] = "System.Boolean";
                                    }
                                    else if (int.TryParse(fieldRowValue, out int fieldRowValueInt))
                                    {
                                        fieldType2[i] = "System.Int32";
                                    }
                                    else if (decimal.TryParse(fieldRowValue, out decimal fieldRowValueDecimal))
                                    {
                                        fieldType2[i] = "System.Double";
                                    }
                                    else if (DateTime.TryParse(fieldRowValue, out DateTime fieldRowValueDateTime))
                                    {
                                        fieldType2[i] = "System.DateTime";
                                    }
                                    else
                                    {
                                        fieldType2[i] = "System.String";
                                    }
                                }

                                //Resolve different types
                                if (fieldType2[0] == fieldType2[1]) { fieldType = fieldType2[0]; }
                                else
                                {
                                    if (fieldType2[0] == null) fieldType = fieldType2[1];
                                    if (fieldType2[1] == null) fieldType = fieldType2[0];
                                    if (fieldType == "") fieldType = "System.String";
                                }

                                //Get the name of the column
                                string? colName = "Column_{0}";
                                try { colName = fieldHeaderValue?.ToString()?.Trim('"'); }
                                catch { colName = string.Format(colName ?? "", colIndex); }

                                //Console.WriteLine($"\n{fieldHeaderValue}");

                                //Add field to the table
                                DataColumn tableColumn = new DataColumn(colName, Type.GetType(fieldType) ?? typeof(string));
                                table?.Columns.Add(tableColumn);
                                colIndex++;
                            }
                        }
                        else
                        {
                            //add values for fields in row then add row
                            tableRow = table!.NewRow();

                            foreach (object? fieldValue in values)
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

                Console.WriteLine($"\nValue {table?.Rows[0][1]} {table?.Rows[0][1].GetType()}");
            }
            else
            {
                Console.WriteLine($"The File at {csvFilePath} Could Not Be Found");
                return 1;
            }


            //Save to Database
            Console.WriteLine($"Creating Table {table?.TableName} in Database");
            await using var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync();

                string? createTableSQL = CreateTableSQL(table!.TableName, table!);
                //Console.WriteLine($"{createTableSQL}");

                using (SqlCommand command = new SqlCommand(createTableSQL, connection))
                    command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }

            Console.WriteLine($"Uploading {table?.Rows.Count} Rows of Data into Table {table?.TableName} in Database");

            try
            {
                SqlBulkCopy bulkcopy = new SqlBulkCopy(connection);
                bulkcopy.DestinationTableName = table?.TableName;

                bulkcopy.WriteToServer(table);
                connection.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                return 1;
            }

            return 0;
        }

        public static string CreateTableSQL(string tableName, DataTable table)
        {
            string sqlsc;
            sqlsc = "\n DROP TABLE IF EXISTS [" + tableName + "];";
            sqlsc += "\n CREATE TABLE [" + tableName + "] (";

            //Check Cell Value
            //Console.WriteLine(table.Rows[1][4].ToString());

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
    }
}