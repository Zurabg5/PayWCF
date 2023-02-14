using System;
using System.Data;
using System.Data.SqlClient;
using System.Xml.Linq;
using System.IO;


namespace PayWCF
{
    public partial class PayService : IPayService
    {

        public static async void WriteLog(string msg)
        {
            
            string OutMsg = DateTime.Now.ToString("HH:mm:ss.fff") + " | " + msg ;
            string Fname = "c:\\Logs\\\\PayWCF_" + (isTesting ? "Test_" : "") + DateTime.Now.ToString("yyyyMMdd") + ".log";
            bool isexit = File.Exists(Fname);

            try
            {
                using (StreamWriter sw = new StreamWriter(Fname, isexit, System.Text.Encoding.UTF8))
                {
                    await sw.WriteLineAsync(OutMsg);
                }
            }
            catch
            {
                return;
            }

        }


        public static string CredDB()
        {

            string conn="";
            try
            {
                XDocument xdoc = XDocument.Load(@"c:\AgFolders\Dbs.config");

                conn = isTesting ? xdoc.Root.Element("CredManTest").Value : xdoc.Root.Element("CredMan").Value;
            }
            catch
            {
                if (isTesting)
                    conn = "data source=192.168.1.195\\PSPRO; initial catalog=CredManTest; user id=iva; password=123456; Pooling = true; Connect Timeout=50; Max Pool Size=1000";
                else
                    conn = "data source=db2.intelexpress.loc; initial catalog = CredMan; user id = iva; password = 123456; Pooling = true; Connect Timeout = 50; Max Pool Size = 1000";
            }
            //Properties.Settings t = new Properties.Settings();
          //  WriteLog("CredDb:" + conn);
            return conn;
        }

        public static string MTDB()
        {

            string conn;
            try
            {
                XDocument xdoc = XDocument.Load(@"c:\AgFolders\Dbs.config");

                //db = isTesting ? xdoc.Root.Element("TestDB").Value : xdoc.Root.Element("WorkDB").Value;
                conn = $"data source = {xdoc.Root.Element("WorkDB").Value}; initial catalog = MTExpress; user id = Iexp7; password = fan_tom1; Pooling = true; Connect Timeout = 50; Max Pool Size = 1000";

            }
            catch
            {
               // if (isTesting)
                 //   conn = "data source=192.168.1.1\\BENARES; initial catalog = MTExpress; user id = Iexp7; password = fan_tom1; Pooling = true; Connect Timeout = 50; Max Pool Size = 1000";
               // else
                    conn = "data source=db1.intelexpress.loc; initial catalog = MTExpress; user id = Iexp7; password = fan_tom1; Pooling = true; Connect Timeout = 50; Max Pool Size = 1000";
            }
            //Properties.Settings t = new Properties.Settings();
         //   WriteLog("CredDb:" + conn);
            return conn;
        }


        public static DataTable getPayTypes()
        {
            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            try
            {
                conn.Open();
                SqlDataAdapter da = new SqlDataAdapter("SELECT * FROM [dbo].[ZG_PayTypes]()", conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;
        }
        private static DataTable getPayHistory(DateTime DT1,DateTime DT2)
        {
            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            try
            {
                conn.Open();
                SqlDataAdapter da = new SqlDataAdapter($"Select * from TBCPay_V where Cast(DTTM  as Date) between Convert(Date,'{DT1.ToString("yyyyMMdd")}') and Convert(Date,'{DT2.ToString("yyyyMMdd")}') order by 1", conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;

        }

        private static DataTable getPayReport(DateTime DT1,DateTime DT2)
        {

            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            try
            {
                conn.Open();
                SqlDataAdapter da = new SqlDataAdapter($"Select * from ZG_TBCPay_V where Cast(DTTM  as Date) between Convert(Date,'{DT1.ToString("yyyyMMdd")}') and Convert(Date,'{DT2.ToString("yyyyMMdd")}') order by 2,1", conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;
        }
        private static DataTable getNashtebi(DateTime DT)
        {
            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            try
            {
                conn.Open();
                SqlDataAdapter da = new SqlDataAdapter($"SELECT * FROM [dbo].[ZG_PayNashti] ('{DT.ToString("yyyy-MM-dd")}') order by 1", conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;

        }

   
    private static DataTable getPaySumm(DateTime DT1, DateTime DT2)
        {

            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            try
            {
                conn.Open();

                //SqlCommand cmd = new SqlCommand($"EXEC [dbo].[ZG_PaySumms] '{DT1.ToString("yyyy-MM-dd")}','{DT2.ToString("yyyy - MM - dd")}'", conn);
/*                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@DT1", DT1.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@DT2", DT2.ToString("yyyy-MM-dd"));*/

                SqlDataAdapter da = new SqlDataAdapter($"EXEC [dbo].[ZG_PaySumms] '{DT1.ToString("yyyy-MM-dd")}','{DT2.ToString("yyyy - MM - dd")}'", conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;



        }
        public DataTable GetLoanaArray(int LoanTypeID, string PrivateID)
        {

            DataTable tb = new DataTable();
            SqlConnection conn = new SqlConnection(CredDB());
            string sql = $@"Select SUBSTRING(Ltrim(C.FirstName),1,1)+'.'+SUBSTRING(Ltrim(C.LastName),1,1)+'.' as Initials, 
Cast(L.ID as int) as LoanID,Cast(L.ExternalID as int) as ID, L.StartDate,L.Limit,dbo.ZG_GetCurrencyNameOfLoanID(L.ID) Curr,ExpirationDate from Loans L
 left join Customers C on C.ID = L.CustomerID
  Where L.ProductTypeID = {LoanTypeID} and L.CustomerID = (Select ID from Customers C Where C.PrivateNumber = '{PrivateID}')
 and StateID = 1 and StatusID in (1,3)  
 order by ExpirationDate";
            try
            {
                conn.Open();
                SqlDataAdapter da = new SqlDataAdapter(sql, conn);
                da.Fill(tb);
                conn.Close();
            }
            catch (Exception x)
            {
                WriteLog(x.Message);
            }
            return tb;

        }
    }
}