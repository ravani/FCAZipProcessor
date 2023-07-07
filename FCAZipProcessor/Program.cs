
using System;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Schema;
using log4net;
using log4net.Config;
using System.Net;
using System.Net.Mail;

class Program
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(Program));
    private static ZipArchiveEntry? partyXmlEntry = null;
    private static ZipArchive? zipArchive = null;
    private static string extractFolderPath = string.Empty;

    static void Main(string[] args)
    {

        XmlConfigurator.Configure(new FileInfo("log4net.config"));
  
        
        string fileZipPath = ConfigurationManager.AppSettings["fileZipPath"];

        bool checkValidity = CheckValidityZIP(fileZipPath);

        //to check validity of input ZIP file
        if (checkValidity)
        {
            SendEmailNotification(true);
            Console.WriteLine("The input ZIP file is valid.");
            Log.Info("Success: The input ZIP file is valid.");

        }
        else
        {
            SendEmailNotification(false);
            Console.WriteLine("The input ZIP file is invalid.");
            Log.Error("Error: The input ZIP file is invalid.");

        }
    }

    static bool CheckValidityZIP(string zipFilePath)
    {
        try
        {
            string[] permittedextensions = ConfigurationManager.AppSettings["permittedExtensions"].Split(',');

            Log.Info("ZIP file validity checks commencing");

            using (zipArchive = ZipFile.OpenRead(zipFilePath))
            {
                //Step 1: Check ZIP contains party.XML
                partyXmlEntry = zipArchive.GetEntry("party.XML");
                if (partyXmlEntry == null)
                {
                    Log.Error("Error: Missing party.XML file in the input ZIP.");
                    return false;
                }

               //Step 2: Check each file in the ZIP is of type permitted extension
                foreach (ZipArchiveEntry entry in zipArchive.Entries)
                {
                    string fileExtension = Path.GetExtension(entry.FullName).ToLower();

                    if(!Array.Exists(permittedextensions, s => string.Equals(s, fileExtension, StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.Error($"Error: The file {entry.FullName} has an invalid extension.");
                        return false;
                    }
                }


                //Step 3: Check if party.XML adheres to provided schema
                string xsdFilePath = ConfigurationManager.AppSettings["fileXSD"];
                bool isXmlValid = ValidateXmlAgainstXsd(partyXmlEntry.Open(), xsdFilePath);
                if (!isXmlValid)
                {
                    Log.Error("Error: The party.XML file is invalid according to the XSD schema.");
                    return false;
                }

                //All steps satisifed; valid ZIP
                Log.Info("Validity checks passed");
                
                //extract to specified location
                ExtractValidZIP();
                
                return true;
            }
        }
        catch (InvalidDataException)
        {
            Log.Error("Error: The ZIP file is invalid or corrupted.");
            return false;
        }
    }

    static void ExtractValidZIP()
    {
        Log.Info("Extracting ZIP commences");

        //fetch application number from party.XML 
        string? applicationNumber = GetApplicationNumberFromPartyXml(partyXmlEntry.Open());

        //specified output folder path from app.config + application no from party.XML + GUID
        extractFolderPath = Path.Combine(ConfigurationManager.AppSettings["extractZIPPath"],$"{applicationNumber}-{Guid.NewGuid().ToString()}");

        zipArchive.ExtractToDirectory(extractFolderPath);

        Log.Info($"Successfully extracted ZIP contents to folder: {extractFolderPath}");
    }

    //validate party.XML against provided party.XSD
    static bool ValidateXmlAgainstXsd(Stream xmlStream, string xsdFilePath)
    {
        try
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.Schemas.Add(null, xsdFilePath);
            settings.ValidationType = ValidationType.Schema;

            using (XmlReader reader = XmlReader.Create(xmlStream, settings))
            {
                while (reader.Read())
                {
                   
                }
            }

            return true;
        }
        catch (Exception)
        {
            // The XML is not well-formed or is not valid according to schema
            return false;
        }
        
    }

    private static string? GetApplicationNumberFromPartyXml(Stream xmlStream)
    {
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(xmlStream);

        // Read the application number from the party.XML file
        XmlNode applicationNumberNode = xmlDoc.SelectSingleNode("/party/applicationno");
        string applicationNumber = applicationNumberNode?.InnerText;

        return applicationNumber;
    }

    private static void SendEmailNotification(bool isValid)
    {
        string? emailSubject = string.Empty;
        string? emailBody = string.Empty;
        string? adminEmail = ConfigurationManager.AppSettings["adminEmail"];
        string? senderEmail = ConfigurationManager.AppSettings["senderEmail"];
        string? senderEmailPassword = ConfigurationManager.AppSettings["senderEmailPassword"];

        //The isValid check here allows us to send the correct email subject/body
        //depending on whether the ZIP was found to be valid or invalid
        if (isValid)
        {
            emailSubject = ConfigurationManager.AppSettings["successEmailSubject"];
            emailBody = ConfigurationManager.AppSettings["successEmailBody"] + extractFolderPath;
        }
        else
        {
            emailSubject = ConfigurationManager.AppSettings["errorEmailSubject"];
            emailBody = ConfigurationManager.AppSettings["errorEmailBody"];
        }
       
       //actually send the email now that we have all the details
        PushEmailNotification(senderEmail, senderEmailPassword, adminEmail, emailSubject, emailBody);

    }

    //Send email using SMTP client
    private static void PushEmailNotification(string? senderEmail, string? senderEmailPassword, string? recipientEmail, string? subject, string? body)
    {
        SmtpClient smtpClient = new SmtpClient("smtp.gmail.com", 587)
        {
            UseDefaultCredentials = false,
            EnableSsl = true,
            Credentials = new NetworkCredential(senderEmail, senderEmailPassword)
        };

        MailMessage mailMessage = new MailMessage(senderEmail, recipientEmail, subject, body);

        try
        {
            smtpClient.Send(mailMessage);
            Log.Info("Email notification sent successfully.");
        }
        catch (Exception ex)
        {
            Log.Error($"Error sending email notification: {ex.Message}");
        }
    }
}
