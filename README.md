# CSV SQL Importer

This tool imports CSV files into SQL Tables and also optionally first downloads them from an FTP site for integrations with cloud systems and provide an easier solution compared with SSIS which will often not work well with large text fields

If you are looking for a tool that works the other way around exporting from SQL into CSV then please see the other project instead:

https://github.com/robinwilson16/CSVSQLExporter

#### Excel Tools

I have also produced Excel tools for importing and exporting which work in the same way as the CSV tools and are available here:

https://github.com/robinwilson16/ExcelSQLImporter

https://github.com/robinwilson16/ExcelSQLExporter

## Purpose

The tool was created as a replacement for Microsoft SQL Integration Services (SSIS) which can work well with smaller files but these days has a lot of limitations which this tool overcomes:
- Excel columns that contain a large number of characters can be exported without any errors or changes being made to settings
- All rows are evaluated when setting column sizes to avoid errors you get with SSIS when the first rows contain less data than subsequent rows and the column size is set to the maximum size needed (for the importer)
- Data types are detected automatically so will export correctly without code page errors, truncated values, missing values where a column mixes text and numbers
- The tool is simpler to use as just requires .NET 9 runtime to be installed and does not require Excel binaries or data access components or any other special settings

## Prereqisites

You will need to install the Microsoft .NET Runtime 9.0 available from: https://dotnet.microsoft.com/en-us/download/dotnet/9.0
Nothing else needs to be installed as this software can just be unzipped and run.

## Setting Up

Download the latest release from: https://github.com/robinwilson16/CSVSQLImporter/releases/latest

If you have an Intel/AMD machine (most likely) then pick the `amd64` version but if you have a an ARM Snapdragon device then pick the `arm64` version.

Download and extract the zip file to a folder of your choice.

Now edit the appsettings.json to fill in details for:
| Item | Purpose |
| ---- | ---- |
| CSV File | Where you are getting the data from |
| Database Connection | Where you are saving the data to |
| Database Table | Allows you to specify a prefix for the SQL table that is created |
| FTP Connection (Optional) | Where you are first downloading the Excel file from if the source is remote rather than local |

Once all settings are entered then just click on `CSVSQLImporter.exe` to run the program.
If you notice any errors appearing in the window then review these, change the settings file and try again.

## Importing Multiple Files

By default configuration values are picked up from `appsettings.json` but in case you want to use the tool to import multiple Database Tables then when running from the commandline specify the name of the config file after the .exe so to pick up settings from a config file called `FinanceImport.json` execute:

```
CSVSQLImporter.exe FinanceImport.json
```

## Setting Up a Schedule

You can just click on may wish to set up a schedule to import one or more CSV files each night and the best way to do this in Windows is to use Task Scheduler which is available in all supported versions of Windows.

Create a new task and name it based on the CSV file it will import so for example:
```
CSVSQLImporter - Finance Data
```

Pick a user account to run the task against. If you used Windows Authentication in your settings file then you will need to pick a user account with sufficient permissions to create the database table you are importing as well as read the CSV file if it is on a network drive.

On the Triggers tab select a schedule such as each day at 18:00.

On the Actions tab specify the location of the CSV SQL Import tool under Program/script (you can use browse to pick it). It should show as something similar to:
```
D:\CSVSQLImporter\CSVSQLImporter.exe
```

Optionally if you are importing more than one file then enter the name of this into the arguments box for each task you set up - e.g.:
```
UsersTable.json
```
