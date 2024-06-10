using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Xml.Linq;
using SMS_Class;
using System.Drawing;
using System.Diagnostics.Eventing.Reader;

namespace PayWCF
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the class name "Service1" in code, svc and config file together.
    // NOTE: In order to launch WCF Test Client for testing this service, please select Service1.svc or Service1.svc.cs at the Solution Explorer and start debugging.
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerSession)]
    public partial class PayService : IPayService
    {
        public static bool isTesting = false;

     public PayService()
        {
            if (System.IO.File.Exists("C:\\AGFolders\\TbcPayTesting.true"))
            {
                isTesting = true;
                WriteLog($"Init=> Testing = {isTesting}");
            }
        }
        #region TBCPay
        public List<LoanTypes> GetLoanTypes()
        {
            List<LoanTypes> result = new List<LoanTypes>();

            var tb = getPayTypes();
            foreach (System.Data.DataRow R in tb.Rows)
            {
                result.Add(new LoanTypes { ID = (int)R["ID"], Name = (string)R["Name"] });
            }

            return result;
        }
        public List<PayRecord> GetPayHistory(DateTime DT1, DateTime DT2)
        {
            List<PayRecord> result = new List<PayWCF.PayRecord>();
            var tb = getPayHistory(DT1, DT2);
            foreach (System.Data.DataRow R in tb.Rows)
            {
                var item = new PayRecord();
                item.ID = (int)R["ID"];
                item.DTTM = (DateTime)R["DTTM"];
                item.PrivateID = (string)R["PrivateID"];
                item.Client = (string)R["Client"];
                item.LoanID = (int)R["LoanID"];
                item.ExternalID = (int)(long)R["ExternalID"];
                item.Terminal = (string)R["Terminal"];
                item.PayType = (int)R["PayType"];
                item.PayTypeName = (string)R["PayTypeName"];
                if (R["TransactionID"] != DBNull.Value)
                    item.TransactionID = (int)R["TransactionID"];
                item.LoanAmount = (decimal)R["LoanAmount"];
                item.PayCore = (decimal)R["PayCore"];
                item.PayPenalty = (decimal)R["PayPenalty"];
                item.PayPercent = (decimal)R["PayPercent"];
                item.InAmount = (decimal)R["InAmount"];
                item.Acumulate = (decimal)R["Acumulate"];
                if (R["NewStatusID"] != DBNull.Value)
                {
                    item.NewStatusID = (int)R["NewStatusID"];
                    item.StatusName = (string)R["StatusName"];
                }
                result.Add(item);
            }

            return result;
        }


        public Result PayAmount(int LoanID, decimal Amount, int PayType, string Terminal, string PrivateID, int Period, string TransactionID)
        {

            int CmPointID = -1;
            if (Terminal == "i-Banking") CmPointID = -2;

            WriteLog($"PayAmount(Loanid:{LoanID},Amount:{Amount}, PayType:{PayType},Terminal:{Terminal},PrivateID:{PrivateID},Period:{Period},TransactionID:{TransactionID}) ...");

            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand($"select count(*) from ZG_TBCPay where PayTRNID='{TransactionID}'", conn);
                int i = (int)cmd.ExecuteScalar();
                if (i == 0)
                {
                    cmd.CommandText = $"select count(*) from ZG_PayOperations where PayTRNID='{TransactionID}'";
                    i = (int)cmd.ExecuteScalar();
                }

                conn.Close();
                if (i > 0)
                {
                    WriteLog($"PayAmount({LoanID}) return => Code:-1, Message:TransactionID:{TransactionID} არ არის უნიკალური");
                    return new Result() { Code = -1, Message = $"TransactionID:{TransactionID} არ არის უნიკალური" };
                }
            }

            string LoanCurrency = GetLoanCurrency(LoanID);

            decimal Rate = (LoanCurrency == "GEL") ? 1 : GetLoanRate(LoanID);
            if (Rate != 1)
                WriteLog($"Loanid:{LoanID}, Rate={Rate}");

            decimal AcumAmount = GetAcumulateAmount(PrivateID);
            OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);
            XDocument xdoc = null;
            decimal LoanAmount; //= Math.Round(Amount / Rate * 100,0) / 100.0m;
                                // WriteLog($"Loanid:{LoanID}, LoanAmount={LoanAmount}");
                                // int inT = (int)(((Amount + AcumAmount) / Rate) * 100);
            decimal ClientAmount = Math.Round((Amount + AcumAmount) / Rate, 2);
            if (Rate!=1 && Math.Round(ClientAmount * Rate,2) > (Amount + AcumAmount))
                    ClientAmount -= 0.01M;
            

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            var l = GetLoanExternalID(LoanID);
            sb.AppendLine($"ხელშეკრულება:{l}<br/>");
            sb.AppendLine("გადახდის ტიპი:" + (PayType == 2 ? "სრული დაფარვა<br/>" : $"{Period} თვის პროლონგაცია<br/>"));
            sb.AppendLine($"შემოტანილი თანხა {Amount.ToString("n2")}<br/>");
            sb.AppendLine($"აკუმულირებული თანხა:{AcumAmount}<br/>");
            if (Rate != 1)
            {
                sb.AppendLine($"სესხის ვალუტა: {LoanCurrency}<br/>");
                sb.AppendLine($"კურსი : {Rate}<br/>");
                sb.AppendLine($"სრული თანხა ვალუტაში:{ClientAmount}<br/>");
            }


            decimal TrnAmount = 0;
            Result result = null;
            string sql = null;
            if (PayType == 2) //Full
            {
                LoanAmount = GetLoanFullAmount(LoanID);
                TrnAmount = LoanAmount;
                WriteLog($"PayType=2, Loanid:{LoanID}, LoanAmount=TrnAmount={TrnAmount}");
                if (LoanAmount > ClientAmount)
                {
                    result = new Result() { Code = 10, Message = $"თანხა {Amount} არ არის საკმარისი სესხის სრულად დასაფარად\r\n ეს თანხა გადავიდა სატრანზიტო ანგარიშზე" };
                    sql = $"insert into ZG_TBCPAY(LoanID,InAmount,accAmount,LoanAmount,PayType,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},0,{PayType},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},0)";
                    if (Amount > 0)
                        AddAcumulateAmount(LoanID, PrivateID, Amount, TransactionID, null);
                    string sms = $"Tkven mier shemotanili tanxa {Amount} GEL arasakmarisia davalianebis sruli dafarvistvis. Gtxovt shemoitanot sxvaoba {Math.Round(LoanAmount * Rate - (Amount + AcumAmount), 2)} GEL, an darekot 2492525.";
                    //tqveni shetanili tanxa {Amount} lari ar aris sakmarisi sesxis srulad dasaparad. es tanxa gadava satranzito angariSze.mis misagebad gTxovT miakiTxoT INTELEXPRESS-is ofiss an sheitanot darchenili Tanxa";
                    //  WriteLog("send sms ...");
                    sb.AppendLine($"დავალიანება:{LoanAmount}<br/>");
                    sb.AppendLine();
                    sb.AppendLine($"<h1 style='color:red;'>{result.Message}</h1>");
                    sendSMS(LoanID, sms);
                    sendMail(sb.ToString());
                }
            }
            else //ProlongationAmount
            {
                string xx = GetPayDataRate(LoanID, Period, Rate);
                XDocument nx = XDocument.Parse(xx);
                LoanAmount = decimal.Parse(nx.Root.Element("ProlongationAmount").Value);
                //  if (Rate!=1) LoanAmount = Math.Round(LoanAmount / Rate * 100,0) / 100.0m;
                //Math.Round(LoanAmount / Rate, 2);
            //    WriteLog($"PayType={PayType}, Loanid:{LoanID}, LoanAmount={LoanAmount}");
                decimal FullAmount = decimal.Parse(nx.Root.Element("FullAmount").Value);
                /*   if (Rate != 1 && (LoanAmount - ClientAmount) == 0.01M)
                       TrnAmount = LoanAmount;
                   else */
                TrnAmount = ClientAmount;  //(int)((Amount+ AcumAmount) / Rate * 100) / 100.0m;
                //(decimal)Math.Round((Amount + AcumAmount) / Rate, 2);

                /*          
                 *          თუ სესხი გაცემულია 2020-ის პირველ მარტამდე, სესხის ძირი არ უნდა შემცირდეს 60 ლარს ქვემოთ.
                 *             აკუმულირებულ თანხას + შემოტანილი თანხა - პროლონგაციის თანხა არის ის რაც აკლდება ძირს
                 *             და ეს ძირის შემცირება 60 ლარს არ უნდა ჩამოცდეს.*/

                if (TrnAmount > LoanAmount)
                {
                    DateTime? Sdate = GetStartDate(LoanID);
                    DateTime ODT = new DateTime(2020, 3, 1);
                    if (Sdate != null && Sdate.Value < ODT)
                    {
                        decimal core = decimal.Parse(nx.Root.Element("CurrentSum").Value);
                        decimal pamount = TrnAmount - LoanAmount;
                        if ((core - pamount) < 60)
                        {
                            TrnAmount = LoanAmount;
                            ClientAmount = TrnAmount * Rate;
                        }
                    }
                }

                //Math.Round(decimal.Parse(nx.Root.Element("ProlongationAmount").Value) * decimal.Parse(nx.Root.Element("Rate").Value),2);
                // TrnAmount = (decimal)Math.Round((Amount + AcumAmount) / Rate, 2);

                if (PayType != 2 && (TrnAmount >= FullAmount)) //|| TrnAmount == 0))
                    TrnAmount = LoanAmount;
                WriteLog($"PayType={PayType}, Loanid:{LoanID}, LoanAmount={LoanAmount}, FullAmount={FullAmount}, TrnAmount={TrnAmount}");

            }
            if (result == null && LoanAmount > TrnAmount) // არ არის საკმარისი თანხა
            {
                AddAcumulateAmount(LoanID, PrivateID, Amount, TransactionID, null);
                string sms = $"Tkven mier shemotanili tanxa {Amount} GEL arasakmarisia sesxis prolongaciistvis. Gtxovt shemoitanot sxvaoba {Math.Round(LoanAmount * Rate - (Amount + AcumAmount), 2)} GEL, an darekot 2492525.";
                //tqveni shetanili tanxa {Amount} lari ar aris sakmarisi davalianebis dasaparad. es tanxa gadava satranzito angariSze.mis misagebad gTxovT miakiTxoT INTELEXPRESS-is ofiss an sheitanot darchenili Tanxa";
              sendSMS(LoanID, sms);
                sql = $"insert into ZG_TBCPAY(LoanID,InAmount,accAmount,LoanAmount,PayType,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},0,{PayType},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},0)";
                result = new Result() { Code = 20, Message = "არ არის საკმარისი თანხა\r\n თანხა გადავიდა სატრანზიტო ანგარიშზე." };
                sb.AppendLine($"დავალიანება:{LoanAmount}<br><br>");
                sb.AppendLine($"<h1 style='color:red;'>{result.Message}</h1>");
                sendMail(sb.ToString());

            }
            if (result == null) // გადახდა 
            {

                xdoc = XDocument.Parse(OV.AddPayment(LoanID.ToString(), TrnAmount, PayType, PrivateID, Period, CmPointID));
                if (xdoc.Root.Element("ERR") != null && xdoc.Root.Element("ERR").Value != "")
                {
                    #region   ვერ მოხდა გადახდა            
                    if /*(PayType == 0 &&*/
                    (xdoc.Root.Element("ERR").Value == "მიმდინარე დავალიანების გასწორებამდე ძირის ნაწილის ჩამოკლება შეუძლებელია!"
                        || xdoc.Root.Element("ERR").Value == "სესხის სრულად დაფარვისთვის გამოიყენეთ შესაბამისი ფუნცქია!")

                    //    )
                    {
                        WriteLog(xdoc.Root.Element("ERR").Value + "\r\nPay step 2 ...");


                        xdoc = XDocument.Parse(OV.AddPayment(LoanID.ToString(), LoanAmount, PayType, PrivateID, Period, CmPointID));
                        if (xdoc.Root.Element("ERR") != null && xdoc.Root.Element("ERR").Value != "")
                        #region მეორეთ ვერ მოხდა გადახდა      
                        {
                            WriteLog(xdoc.Root.Element("ERR").Value);
                            AddAcumulateAmount(LoanID, PrivateID, Amount, TransactionID, null);
                            string sms = $"gadaxda ver ganxorcielda, tqveni shetanili tanxa {Amount} lari gadava satranzito angariSze.mis misagebad gTxovT miakiTxoT INTELEXPRESS-is ofiss an sheitanot darchenili Tanxa";
                            sendSMS(LoanID, sms);
                            sql = $"insert into ZG_TBCPAY(LoanID,InAmount,AccAmount,LoanAmount,PayType,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},0,{PayType},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},0)";
                            result = new Result() { Code = 20, Message = xdoc.Root.Element("ERR").Value + "\r\nთანხა გადავიდა სატრანზიტო ანგარიშზე. მის მისაღებად გთხოვთ მიაკითხოთ \"ინტელექსპრესი\"-ს ოფისებს" };
                            sb.AppendLine($"დავალიანება:{LoanAmount}<br><br>");
                            sb.AppendLine($"<h1 style='color:red;'>კრედმენის შეცდომა:{xdoc.Root.Element("ERR").Value}</h1>");
                            sendMail(sb.ToString());

                        }
                        #endregion
                        else
                        #region გადახდა მოხდა მეორე ჯერზე
                        {
                            int TrnID = int.Parse(xdoc.Root.Element("TransactionID").Value);
                            decimal NatLoanAmount = LoanAmount;
                            if (Rate != 1)
                            {
                                NatLoanAmount = Math.Round((LoanAmount * Rate) + 0.005M, 2);
                            }

                            sql = $"insert into ZG_TBCPAY(LoanID,InAmount,AccAmount,LoanAmount,PayType,TransactionID,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},{LoanAmount},{PayType},{TrnID},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},{NatLoanAmount})";
                            result = new Result() { Code = 0, Message = "OK" };
                            if (NatLoanAmount != Amount)
                            {
                                if (NatLoanAmount < Amount)
                                    AddAcumulateAmount(LoanID, PrivateID, Amount - NatLoanAmount, TransactionID, TrnID);
                                else
                                    if (AcumAmount >= NatLoanAmount - Amount)
                                    SubAcumulateAmount(LoanID, PrivateID, (NatLoanAmount - Amount), TransactionID, TrnID);
                                else
                            if (AcumAmount > 0 && NatLoanAmount > Amount)
                                    SubAcumulateAmount(LoanID, PrivateID, -AcumAmount, TransactionID, TrnID);
                            }
                        }
                        #endregion
                    }
                    else
                    {
                        AddAcumulateAmount(LoanID, PrivateID, Amount, TransactionID, null);
                        string sms = $"gadaxda ver ganxorcielda, tqveni shetanili tanxa {Amount} lari gadava satranzito angariSze.mis misagebad gTxovT miakiTxoT INTELEXPRESS-is ofiss an sheitanot darchenili Tanxa";
                        sendSMS(LoanID, sms);
                        sql = $"insert into ZG_TBCPAY(LoanID,InAmount,AccAmount,LoanAmount,PayType,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},0,{PayType},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},0)";
                        result = new Result() { Code = 20, Message = xdoc.Root.Element("ERR").Value + "\r\nთანხა გადავიდა სატრანზიტო ანგარიშზე. მის მისაღებად გთხოვთ მიაკითხოთ \"ინტელექსპრესი\"-ს ოფისებს" };
                        sb.AppendLine($"დავალიანება:{LoanAmount}<br><br>");
                        sb.AppendLine($"<h3 style='color:red;'>კრედმენის შეცდომა:{xdoc.Root.Element("ERR").Value}</h3>");
                        sendMail(sb.ToString());

                    }

                    #endregion
                }

                else // წარმატებილი გადახდა 
                {
                    int TrnID = int.Parse(xdoc.Root.Element("TransactionID").Value);
                    result = new Result() { Code = 0, Message = "OK" };
                    decimal NatLoanAmount = TrnAmount;
                    if (Rate != 1)
                    {
                        NatLoanAmount = Math.Round((TrnAmount * Rate) + 0.005M, 2);
                        if (NatLoanAmount > Amount+ AcumAmount) NatLoanAmount = Amount+ AcumAmount;
                    }

                    sql = $"insert into ZG_TBCPAY(LoanID,InAmount,accAmount,LoanAmount,PayType,TransactionID,Terminal,PrivateID,PayTRNID,Rate,GEL) Values({LoanID},{Amount},{AcumAmount},{TrnAmount},{PayType},{TrnID},'{Terminal}','{PrivateID}','{TransactionID}',{Rate},{NatLoanAmount})";


                    //decimal NatLoanAmount = Math.Round(TrnAmount * Rate, 2);
                    WriteLog($"NatTrnAmount={NatLoanAmount}");

                    if (NatLoanAmount != Amount)
                    {
                        if (NatLoanAmount < Amount)
                            AddAcumulateAmount(LoanID, PrivateID, Amount - NatLoanAmount, TransactionID, TrnID);
                        else
                            if ((NatLoanAmount > Amount) && (Amount + AcumAmount) > NatLoanAmount)
                            SubAcumulateAmount(LoanID, PrivateID, (NatLoanAmount - Amount), TransactionID, TrnID);
                        else
                    if (AcumAmount > 0 && NatLoanAmount > Amount)
                            SubAcumulateAmount(LoanID, PrivateID, AcumAmount, TransactionID, TrnID);
                    }
                }
            }
            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                try
                {
                    conn.Open();
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    int i = cmd.ExecuteNonQuery();
                    conn.Close();
                }
                catch (Exception x)
                { WriteLog($"ERR: {x.Message} +> SQL\r\n{sql}"); }

            }

            WriteLog($"PayAmount({LoanID}) return => Code:{result.Code}, Message:{result.Message}");
            if (result.Code == 0 && xdoc.Root.Element("NewStatusID").Value == "3")
            {
                string sms = $"Xelsh. #{xdoc.Root.Element("ExternalID").Value} -ze shemotanili tanxa {LoanAmount} GEL ar yofnis davalianebis dafarvas. Daifara mxolod {Period} tvis davalianeba, ris gamoc kvlav dagericxebat jarima. T: 2492525";
                //tqven sheitanaet {LoanAmount} lari xelshekrulebaze #{xdoc.Root.Element("ExternalID").Value} magram es tanxa ar aris sakmarisi davalianebis srulad dasaparad, ris gamoc kvlav dagericxebad jarima";
                sendSMS(LoanID, sms);
            }
            return result;
        }
         private string GetPayDataRate(int LoanID, int Period, decimal Rate)
        {
            try
            {
                OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);//comboBox2.SelectedItem.ToString());
                return OV.getPawnData(LoanID, Period);
            }
            catch (Exception x)
            {
                string s = $"ERR:{x.Message}";
                WriteLog(s);
                return s;
            }
        }

        public string GetPayData(int LoanID, int Period)
        {
            int step = 0;
            try
            {
                OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);//comboBox2.SelectedItem.ToString());
                step++;
                string s = OV.getPawnData(LoanID, Period);
                XDocument xdoc = XDocument.Parse(s);
                decimal Rate = decimal.Parse(xdoc.Root.Element("Rate").Value);
                if (Rate != 1.0M)
                {
                    setLoanRate(LoanID, Rate);
                    decimal d = decimal.Parse(xdoc.Root.Element("FullAmount").Value);
                    xdoc.Root.Element("FullAmount").Value = Math.Round((d * Rate) + 0.004M, 2).ToString();

                    d = decimal.Parse(xdoc.Root.Element("Penalty").Value);
                    if (d > 0) xdoc.Root.Element("Penalty").Value = Math.Round((d * Rate) + 0.005M, 2).ToString();

                    d = decimal.Parse(xdoc.Root.Element("Percent").Value);
                    if (d > 0) xdoc.Root.Element("Percent").Value = Math.Round((d * Rate) + 0.005M, 2).ToString();

                    d = decimal.Parse(xdoc.Root.Element("CurrentSum").Value);
                    if (d > 0) xdoc.Root.Element("CurrentSum").Value = Math.Round((d * Rate) + 0.005M, 2).ToString();

                    d = decimal.Parse(xdoc.Root.Element("ProlongationAmount").Value);
                    if (d > 0)
                    {
                        decimal gel = Math.Round((d * Rate) + 0.005M, 2);
                        decimal d1 = Math.Round(gel / Rate,2);
                        if (d1 > d)
                            gel += Math.Round((d1 * Rate), 2); ;

                        xdoc.Root.Element("ProlongationAmount").Value = gel.ToString();
                    }
                    d = decimal.Parse(xdoc.Root.Element("PrProc").Value);
                    if (d > 0) xdoc.Root.Element("PrProc").Value = Math.Round((d * Rate) + 0.005M, 2).ToString();
                    s = xdoc.ToString();
                }
                WriteLog($"GetPayData(LoanID:{LoanID}, Period:{Period}) => {s}");
                return s;
            }
            catch (Exception x)
            {
                string s = $"step:{step} ERR:{x.Message}";
                WriteLog(s);
                return s;
            }
        }

       
        private int LoanIDFromExternalID(string PrivateID, int ID, out string Client)
        {
            int result = 0;
            string sql = $@"select L.ID,C.FullName from Loans L
left Join Customers C on C.ID = L.CustomerID
 where ExternalID = {ID} and C.PrivateNumber = '{PrivateID}' and L.StatusID in (1,3) and ProductTypeID = 3";

            using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();

                SqlDataReader dr = cmd.ExecuteReader();
                if (dr.HasRows)
                {
                    dr.Read();
                    result = (int)((long)dr["ID"]);
                    Client = (string)dr["FullName"];
                }
                else Client = null;
                cmd.Connection.Close();
            }
            return result;
        }
        public long? getClientLoanIdFromExternalID(string Pid, long ID)
        {
            long? result = null;
            string sql = $@"select L.ID from Loans L
                        left Join Customers C on C.ID = L.CustomerID
                    where ExternalID = {ID} and C.PrivateNumber = '{Pid}' and L.StatusID in (1,3)";
            using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();
                var r = cmd.ExecuteScalar();
                cmd.Connection.Close();
                if (r != null && r != DBNull.Value) result = (long)r;
            }
            return result;
        }

            public LoanList GetLoans(int TypeID, string PrivateID, out string info)
        {
            WriteLog($"GetLoans(TypeID:{TypeID},PrivateID:{PrivateID}) ....");
            info = "";
            //decimal acumulate = GetAcumulateAmount(PrivateID);
            LoanList result = new LoanList();
            //     if (PrivateID == "66666666666" || PrivateID == "77777777777" || PrivateID == "88888888888" || PrivateID == "99999999999" || PrivateID == "01008024543")
            // {
            var tb = GetLoanaArray(TypeID, PrivateID);
            if (tb.Rows.Count > 0)
            {
                result.Client = (string)tb.Rows[0]["Initials"];
                result.AcumulateAmount = GetAcumulateAmount(PrivateID);
                result.Loans = new List<Loan>();
                foreach (System.Data.DataRow R in tb.Rows)
                {
                    Loan item = new PayWCF.Loan();
                    item.ID = (int)R["ID"];
                    item.LoanID = (int)R["LoanID"];
                    item.Currency = (string)R["Curr"];
                    item.LoanAmount = (decimal)R["Limit"];
                    item.CreateDate = ((DateTime)R["StartDate"]).Date;
                    item.NextPayDate = R["ExpirationDate"] != DBNull.Value ? ((DateTime)R["ExpirationDate"]).Date : (DateTime?)null;
                    result.Loans.Add(item);
                    string res = $"\t\t ID={item.ID}, LoanID:{item.LoanID}, Currency:{item.Currency}, LoanAmount:{item.LoanAmount}";
                    WriteLog(res);
                }
            }

            else
            {
                info = "მონაცემი ვერ მოიძებნა, გთხოვთ გადაამოწმოთ"; // არ არის სესხი თქვენზე";
                WriteLog(info);
            }
            /* }
             else
             {
                 info = "არ არის სესხი თქვენზე";
                 WriteLog(info);
             }*/

            return result;

        }
        public decimal GetAcumulateAmount(string PrivateID)
        {
            decimal result = 0;
            string sql = $"select isnull(sum(Amount),0) from ZG_PayOperations where CustomerID=(select ID from customers where PrivateNumber='{PrivateID}')";
            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                conn.Open();
                //                string sql = $"Select dbo.Check_CreditPhone(Phone1) from customers where ID=(select customerID from Loans where ID={LoanID})";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    result = (decimal)cmd.ExecuteScalar();
                    conn.Close();

                }
            }
            WriteLog($"GetAccumulateAmount(PrivateID:{PrivateID}) => {result}");
            return result;
        }
        public string GetLoanFullAmounts(int LoanID)
        {
            string resultStr = null;
            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand($"Select CurrentSum,CurrentPercent,MinimumPayment,CurrentPenalty,[dbo].[ZG_GetCurrencyNameOfLoanID]({LoanID}) as Curr from Loans Where ID={LoanID}", conn))
                {
                    decimal Rate = 1;
                    SqlDataReader dr = cmd.ExecuteReader();
                    if (dr.HasRows)
                    {
                        dr.Read();
                        decimal cur = (decimal)dr["CurrentSum"];
                        decimal per = (decimal)dr["CurrentPercent"];
                        decimal pen = (decimal)dr["CurrentPenalty"];
                        decimal minper = (decimal)dr["MinimumPayment"];
                        string Curr = (string)dr["Curr"];

                        if (Curr != "GEL")
                        {
                            Rate = GetCurrentRate(Curr);
                            setLoanRate((int)LoanID, Rate);
                            if (cur != 0) cur = Math.Round((cur * Rate) + 0.004M, 2);
                            if (per != 0) per = Math.Round((per * Rate) + 0.004M, 2);
                            if (pen != 0) pen = Math.Round((pen * Rate) + 0.004M, 2);
                            if (minper != 0) minper = Math.Round((minper * Rate) + 0.004M, 2);
                        }
                        decimal FullAmt;
                        OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);
                        FullAmt = OV.getPawnFullAmount(LoanID);



/*                        if (per + pen >= 0.01M && (per + pen) < minper)
                            FullAmt = cur + minper;
                        else
                            FullAmt = cur + pen + per;*/
                        XDocument xdoc = new XDocument(
                        new XElement("Root",
                        new XElement("Core", cur),
                        new XElement("Percent", per),
                        new XElement("Penalty", pen),
                        new XElement("Full", FullAmt)
                        )
                        );

                        resultStr = xdoc.ToString();
                    }
                    dr.Close();
                }
                conn.Close();
            }
            WriteLog($"GetLoanFullAmounts({LoanID}) => {resultStr}");
            return resultStr;
        }


        public string GetLoanInfo(int LoanID)
        {
            OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);//comboBox2.SelectedItem.ToString());
            string s = OV.GetLoanData(LoanID, 1);
            XDocument xdoc = XDocument.Parse(s);
            XDocument ResX = new XDocument("Info");
            WriteLog($"GetLoanInfo({LoanID}) => {s}");

            return s;

        }
        #endregion

        #region i-Banking
        public Result PayAmountFromID(int ID, decimal Amount, int PayType, string PrivateID, string TransactionID)
        {
            if (PayType == 1) PayType = 0;

            WriteLog($"PayAmount(ExternalID :{ID},Amount:{Amount}, PayType:{PayType},Terminal:i-Banking,PrivateID:{PrivateID},TransactionID:{TransactionID}) ...");

            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand($"select count(*) from ZG_TBCPay where PayTRNID='{TransactionID}'", conn);
                int i = (int)cmd.ExecuteScalar();
                if (i == 0)
                {
                    cmd.CommandText = $"select count(*) from ZG_PayOperations where PayTRNID='{TransactionID}'";
                    i = (int)cmd.ExecuteScalar();
                }

                conn.Close();
                if (i > 0)
                {
                    WriteLog($"PayAmountFromID({ID}) return => Code:-1, Message:TransactionID:{TransactionID} არ არის უნიკალური");
                    return new Result() { Code = -1, Message = $"TransactionID:{TransactionID} არ არის უნიკალური" };
                }
            }
            string Text;
            int LoanID = LoanIDFromExternalID(PrivateID, ID, out Text);
            if (LoanID==0)
            {
                return new Result() { Code = -2, Message = $"ვერ მოინახა აქტიური ხელშეკრულება #{ID}" };
            
            }

           int Period = 1;
            if (PayType != 2)
            {

             XDocument  xdoc = XDocument.Parse(GetPayData(LoanID, -1));
                Period = int.Parse(xdoc.Root.Element("Month").Value);
            }
           Result result = PayAmount(LoanID, Amount, PayType, "i-Banking", PrivateID, Period, TransactionID);
            return result;

        }
        public string GetPayDataFromID(string PrivateID, int ID)
        {

        /*    if (PrivateID != "01008024543")
            {
                XElement xdoc = new XElement("LoanData",
                    new XElement("ERROR", "ტესტირების რეჟიმი! პირადი ნომერი ან სესხის ნომერი არასწორია"));
                return xdoc.ToString();
            }
            */
            string result = null, Client;
            int LoanID = LoanIDFromExternalID(PrivateID, ID, out Client);



            if (LoanID > 0 && Client != null)
            {
                decimal AcumAmount = GetAcumulateAmount(PrivateID);

                XDocument xdoc = XDocument.Parse(GetPayData(Convert.ToInt32((long)LoanID), -1));
                decimal PAmount = decimal.Parse(xdoc.Root.Element("ProlongationAmount").Value);
                XDocument ox = new XDocument(
                    new XElement("Prolongation",
                    new XElement("ID", ID),
                    new XElement("ClientName", Client),
                    new XElement("Month",xdoc.Root.Element("Month").Value),
                    new XElement("ProlongationAmount", PAmount),
                    new XElement("AcumulateAmount", AcumAmount),
                    new XElement("PayAmount", PAmount - AcumAmount)));
                result = ox.ToString();
            }
            else
            {
                XElement xdoc = new XElement("LoanData",
                    new XElement("ERROR", "პირადი ნომერი ან სესხის ნომერი არასწორია"));
                result = xdoc.ToString();
            }
            return result;
        }
        public string GetFullAmountFromID(string PrivateID, int ID)
        {
            string result = null, Client;

            int LoanID = LoanIDFromExternalID(PrivateID, ID, out Client);
            if (LoanID > 0 && Client != null)
            {
                XDocument xdoc = XDocument.Parse(GetLoanFullAmounts(LoanID));
                decimal AcumAmount = GetAcumulateAmount(PrivateID);
                decimal PAmount = decimal.Parse(xdoc.Root.Element("Full").Value);
                XDocument xx = new XDocument(
                    new XElement("Full",
                    new XElement("ID", ID),
                    new XElement("ClientName", Client),
                        new XElement("FullAmount", PAmount),
                        new XElement("AcumulateAmount", AcumAmount),
                        new XElement("PayAmount", PAmount - AcumAmount)));
                result = xx.ToString();
            }
            else
            {
                XElement xdoc = new XElement("LoanData",
                    new XElement("ERROR", "პირადი ნომერი ან სესხის ნომერი არასწორია"));
                result = xdoc.ToString();
            }
            return result;
        }

        #endregion

        #region Private
        private void AddAcumulateAmount(int LoanID, string PrivateID, decimal Amount, string PayID, int? TrnID)
        {
            if (Amount == 0.0M) return;
            WriteLog($"AddAccumulateAmount(PrivateID:{PrivateID}, Amount:{Amount},PayID:{PayID})");
            string sql;
            if (TrnID == null)
                sql = $"insert into ZG_PayOperations([CustomerID],[OperationType],LoanID,[Amount],[PayTrnID]) select ID,1,{LoanID},{Amount},'{PayID}' from customers where PrivateNumber='{PrivateID}'";
            else
                sql = $"insert into ZG_PayOperations([CustomerID],[OperationType],LoanID,[Amount],[PayTrnID],TransactionID) select ID,1,{LoanID},{Amount},'{PayID}',{TrnID} from customers where PrivateNumber='{PrivateID}'";
            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                try
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.ExecuteNonQuery();
                        conn.Close();

                    }
                }
                catch (Exception x)
                { WriteLog($"ERR: {x.Message} +> SQL\r\n{sql}"); }
            }
        }
        private void SubAcumulateAmount(int LoanID, string PrivateID, decimal Amount, string PayID, int? TrnID)
        {
            if (Amount == 0.0M) return;
            WriteLog($"SubAccumulateAmount(PrivateID:{PrivateID}, Amount:{Amount},PayID:{PayID})");

            string sql;

            if (TrnID == null)
                sql = $"insert into ZG_PayOperations([CustomerID],[OperationType],LoanID,[Amount],[PayTrnID]) select ID,2,{LoanID},-{Math.Abs(Amount)},'{PayID}' from customers where PrivateNumber='{PrivateID}'";
            else
                sql = $"insert into ZG_PayOperations([CustomerID],[OperationType],LoanID,[Amount],[PayTrnID],TransactionID) select ID,2,{LoanID},-{Math.Abs(Amount)},'{PayID}',{TrnID} from customers where PrivateNumber='{PrivateID}'";
            using (SqlConnection conn = new SqlConnection(CredDB()))
            {
                try
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.ExecuteNonQuery();
                        conn.Close();

                    }
                }
                catch (Exception x)
                { WriteLog($"ERR: {x.Message} +> SQL\r\n{sql}"); }

            }

        }

        private string GetLoanCurrency(int LoanID)
        {
            string Curr, sql = $"select [dbo].[ZG_GetCurrencyNameOfLoanID]({LoanID})";
            using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();
                Curr = (string)cmd.ExecuteScalar();
                cmd.Connection.Close();
            }
            return Curr;
        }
        private decimal GetCurrencyRateL(int LoanID)
        {
            decimal Rate = 1;
            string Curr = GetLoanCurrency(LoanID);
            if (Curr != "GEL")
                Rate = GetLoanRate(LoanID);
            return Rate;
        }


        private DateTime? GetStartDate(int LoanID)
        {
            DateTime? DT = null;
            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = new SqlConnection(CredDB());
                cmd.Connection.Open();
                cmd.CommandText = $"Select StartDate From Loans Where ID={LoanID}";
                var dt = cmd.ExecuteScalar();
                cmd.Connection.Close();
                if (dt != DBNull.Value)
                    DT = ((DateTime)dt).Date;
            }
            return DT;
        }
        private XDocument GetLoanInfo(int LoanID, int Period)
        {
            OverdraftLib.Overdraft WS = new OverdraftLib.Overdraft(isTesting);
            string s = WS.GetLoanDataToPay(LoanID, Period);
            XDocument result = XDocument.Parse(s);
            WriteLog($"GetLoanInfo(LoanID:{LoanID},Period:{Period} => s");
            return result;

        }

        private decimal GetCurrentRate(string Currency)
        {
            decimal d = 0;
            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = new SqlConnection(MTDB());
                cmd.Connection.Open();
                cmd.CommandText = $"Select Rate From Rate Where Active=1 and PointID=8 and AgentID=-1 and RateType=0 and ValCell=0 and ValBuy = (Select CurrencyID From Currency C Where C.Simbol='{Currency}')";
                var v = cmd.ExecuteScalar();
                if (v != DBNull.Value)
                    d = Convert.ToDecimal(v);
            }
            return d;
        }

        private decimal GetLoanRate(int LoanID)
        {
            decimal d = 0;
            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = new SqlConnection(CredDB());
                cmd.Connection.Open();
                cmd.CommandText = $"Select Rate From ZG_TBCPayLRate Where LID={LoanID}";
                var v = cmd.ExecuteScalar();
                if (v != DBNull.Value)
                    d = Convert.ToDecimal(v);
            }
            return d;
        }


        private void setLoanRate(int LoanID, decimal Rate)
        {
            using (SqlCommand cmd = new SqlCommand())
            {
                cmd.Connection = new SqlConnection(CredDB());
                cmd.Connection.Open();
                cmd.CommandText = "[dbo].[ZG_SetTbcPayLRate]";
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@LoanID", LoanID);
                cmd.Parameters.AddWithValue("@LRate", Rate);
                cmd.ExecuteNonQuery();
                cmd.Connection.Close();
            }
        }


        private decimal GetLoanFullAmount(int LoanID)
        {

            OverdraftLib.Overdraft OV = new OverdraftLib.Overdraft(isTesting);
            decimal fullAmount = OV.getPawnFullAmount(LoanID);

            /*string SQL = $"Select CurrentSum+case when MinimumPayment > CurrentPercent then MinimumPayment else CurrentPercent end + CurrentPenalty from Loans Where ID={LoanID}";
            using (SqlCommand cmd = new SqlCommand(SQL, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();
                fullAmount = (decimal)cmd.ExecuteScalar();
                cmd.Connection.Close();
            }
            */
            WriteLog($"GetLoanFullAmount(LoanID:{LoanID}) => {fullAmount}");
            return fullAmount;
        }

        private long GetLoanExternalID(int LoanID)
        {
            long result;
            string SQL = $"Select ExternalID from Loans where ID={LoanID}";
            using (SqlCommand cmd = new SqlCommand(SQL, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();
                result = (long)cmd.ExecuteScalar();
                cmd.Connection.Close();
            }
            return result;
        }


        private static async void sendSMS(int LoanID, string sms)
        {
            string phone = null; int step = 0;
            if (isTesting) return;
#if DEBUG
                //phone = "995577441417";
                return;
#endif
            try
            {

                string sql = $"Select dbo.Check_CreditPhone(Phone1) from customers where ID=(select customerID from Loans where ID={LoanID})";
            step++;
                using (SqlConnection conn = new SqlConnection(CredDB()))
                {
                    step++;
                    conn.Open();
                    step++;
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        step++;
                        phone = await Task.Run(() => (string)cmd.ExecuteScalar());
                        step++;
                        conn.Close();
                        step++;
                    }
                }

            if (phone == null && phone.Length < 12) { WriteLog($"Phone:{phone} is not correct"); return; }

                SMS_Class.SMS_Class smscl = new SMS_Class.SMS_Class();
                await Task.Run(() => smscl.SendmagtiSMS(phone, sms, "1", null));
                step++;
                WriteLog($"Send Phone:{phone} sms:{sms }");
                step++;
            }
            catch (Exception x)
            { WriteLog($"Step:{step}; ERR:{x.Message}"); }
        }

        private static void SendMail(string From, string To, string subject, string body)
        {
            int step = 0;
            System.Net.ServicePointManager.ServerCertificateValidationCallback += (o, c, ch, er) => true;
            try
            {

                System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage();
                step++;
                mail.IsBodyHtml = true;
                step++;
                mail.From = new System.Net.Mail.MailAddress(From + "<zurabg@intelexpress.ge>");
                step++;
                string[] to = To.Split(';');
                step = 10;
                mail.Bcc.Add(new System.Net.Mail.MailAddress("zurabg@intelexpress.ge"));
                foreach (string toaddr in to)
                {
                    mail.To.Add(new System.Net.Mail.MailAddress(toaddr));
                    step++;
                }
                step = 20;
                mail.Subject = subject;
                step++;
                mail.Body = body;
                step++;
                string SmtpAddr = "smtp.gmail.com";
                step++;
                System.Net.Mail.SmtpClient client = new System.Net.Mail.SmtpClient(SmtpAddr);
                step++;
                client.Port = 587;
                step++;
                client.EnableSsl = true;
                step++;
                client.Credentials = new System.Net.NetworkCredential("zurabg@intelexpress.ge", "12zu+=r76");
                step++;

                client.Send(mail);
            }
            catch (Exception x)
            {
                 WriteLog($"STEP:{step} Sendmail(from:zurabg@intelexpress.ge, To:{To},subject:{subject},body:{body}) => ERR:{x.Message} ");
            }
        }


        private static void sendMail(string body)
        {
            XDocument xdoc = XDocument.Load(@"c:\Agfolders\TBCPay.config");

            System.Net.ServicePointManager.ServerCertificateValidationCallback += (o, c, ch, er) => true;
            try
            {

                System.Net.Mail.MailMessage mail = new System.Net.Mail.MailMessage();
                mail.IsBodyHtml = true;
                mail.From = new System.Net.Mail.MailAddress(xdoc.Root.Element("from").Value);
                string[] to = xdoc.Root.Element("to").Value.Split(';');

                if (xdoc.Root.Element("bcc")!=null && xdoc.Root.Element("bcc").Value!="")
                    mail.Bcc.Add(new System.Net.Mail.MailAddress(xdoc.Root.Element("bcc").Value));

                foreach (string toaddr in to)
                {
                    if (toaddr!="")
                    mail.To.Add(new System.Net.Mail.MailAddress(toaddr));
                    
                }
                mail.Subject = xdoc.Root.Element("Subject").Value;
                mail.Body = body;
                string SmtpAddr = xdoc.Root.Element("SMTP").Value; // "smtp.gmail.com";
                System.Net.Mail.SmtpClient client = new System.Net.Mail.SmtpClient(SmtpAddr);
                client.Port = int.Parse(xdoc.Root.Element("SMTP").Attribute("Port").Value);
                client.EnableSsl = bool.Parse(xdoc.Root.Element("SMTP").Attribute("SSL").Value);
                client.Credentials = new System.Net.NetworkCredential(xdoc.Root.Element("Crediental").Value, xdoc.Root.Element("Crediental").Attribute("pwd").Value);
                client.Send(mail);
            }

            catch (Exception x)
            {
                 WriteLog($"Sendmail ERR:{x.Message}");
            }


//            SendMail("TBCPay Service", "kakhaber@intelexpress.ge;iva@intelexpress.ge", "TBCPay-ს შეცდომა", body);
          //  SendMail("TBCPay Service", "zurabgz@gmail.com", "TBCPay-ს შეცდომა", body);
        }
#endregion
        /***************  R E P O R T S  ***************/
#region Reports
        public List<ReportRecord> GetPayReport(DateTime DT1, DateTime DT2)
        {
            List<ReportRecord> result = new List<ReportRecord>();
            int step = 0;
            try
            {
                var tb = getPayReport(DT1, DT2);
                step++;
                foreach (System.Data.DataRow R in tb.Rows)
                {
                    step++;
                    var item = new ReportRecord();

                    item.ID = (int)R["ID"];
                    item.DT = (DateTime)R["DTTM"];
                    item.FullName = (string)R["FullName"];
                    item.CustomerID = (long)R["CustomerID"];
                    item.PrivateID = (string)R["PrivateID"];
                    item.OperationType = (int)R["OperationType"];
                    item.Amount = (decimal)R["Amount"];
                    item.PayTrnID = R["PayTrnID"] != DBNull.Value ? (string)R["PayTrnID"] : null;
                    item.TransactionID = R["TransactionID"] != DBNull.Value ? (int)R["TransactionID"] : (long?)null;
                    item.LoanID = R["LoanID"] != DBNull.Value ? (int)R["LoanID"] : (int?)null;
                    item.ExternalID = R["ExternalID"] != DBNull.Value ? (long)R["ExternalID"] : (long?)null;
                    item.accAmount = (decimal)R["accAmount"];
                    item.InAmount = (decimal)R["InAmount"];
                    item.LoanAmount = (decimal)R["LoanAmount"];
                    item.GEL = (decimal)R["GEL"];
                    item.NewAcc = (decimal)R["NewAcc"];
                    item.otherOut = R["otherOut"] != DBNull.Value ? (decimal)R["otherOut"] : (decimal?)null;
                    item.PayType = (int)R["PayType"];
                    item.PayOperation = R["PayOperation"] != DBNull.Value ? (string)R["PayOperation"] : null;
                    item.Terminal = R["Terminal"] != DBNull.Value ? (string)R["Terminal"] : null;
                    item.Rate = (double)R["Rate"];
                    item.Currency = (string)R["Currency"];
                    item.statusName = (string)R["statusname"];

                    result.Add(item);
                    step++;
                }
            }
            catch (Exception x)
            { WriteLog($"GetPayReport Step:{step} => {x.Message}"); }
            return result;
        }

        public List<PaySumRecord> GetPaySum(DateTime DT1, DateTime DT2)
        {
            List<PaySumRecord> result = new List<PaySumRecord>();
            var tb = getPaySumm(DT1, DT2);
            foreach (System.Data.DataRow R in tb.Rows)
            {
                var item = new PaySumRecord()
                {
                    DT = (DateTime)R[0],
                    CCY = (string)R[1],
                    inAmount = R[2] != DBNull.Value ? (decimal)R[2] : (decimal?)null,
                    OutAmount = R[3] != DBNull.Value ? (decimal)R[3] : (decimal?)null,
                    OutGel = R[4] != DBNull.Value ? (decimal)R[4] : (decimal?)null
                };
                result.Add(item);
            }
            return result;
        }

        public string getPayConvertation(DateTime DT1, DateTime DT2)
        {
            string result = null,
                sql = $"SELECT [dbo].[ZG_Convertation] ('{DT1.ToString("yyyy-MM-dd")}','{DT2.ToString("yyyy-MM-dd")}')";

            try
            {
                using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
                {
                    cmd.Connection.Open();
                    var res = cmd.ExecuteScalar();
                    cmd.Connection.Close();
                    if (res != null && res != DBNull.Value)
                        result = (string)res;
                }
            }
            catch (Exception x)

            {
                result = x.Message;
                WriteLog($"ERR: {result} +> SQL\r\n{sql}");
            }

            return result;
        }

        public List<Nashti> getNashti(DateTime DT)
        {
            List<Nashti> result = new List<Nashti>();
            var tb = getNashtebi(DT);
            foreach (System.Data.DataRow R in tb.Rows)
            {
                var item = new Nashti()
                {
                    FullName = (string)R[0],
                    PrivateID = (string)R[1],
                    Amount = (decimal)R[2]
                };
                result.Add(item);
            }
            return result;
        }
        public int? getCMID(int MTID)
        {
            int? result = null;
            string sql = $"select CMID from ZG_PayUsers where MTID = {MTID} and EnableMenu = 1";
            using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
            {
                cmd.Connection.Open();
                var res = cmd.ExecuteScalar();
                cmd.Connection.Close();
                if (res != null && res != DBNull.Value)
                    result = (int)res;
                return result;
            }
        }

        public void OutAccAmountLoan(string PrivateID, decimal Amount, int CMID, long LoanID, long ExternalID, int outType)
        {
            try
            {
                string sql = $"select FirstName+' '+Lastname from users where ID={CMID}";  
                using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
                {
                    cmd.Connection.Open();
                    string res = cmd.ExecuteScalar().ToString();
                    if (res.Length > 20) res = res.Substring(0, 20);

                    cmd.CommandText = $@"insert into ZG_PayOperations([CustomerID],[OperationType],UID,[Amount],[PayTrnID],LoanID,ExternalID) 
                    select ID,{outType},{CMID},-{Amount},N'{res}',{LoanID},{ExternalID} from customers where PrivateNumber='{PrivateID}'";
                    cmd.ExecuteNonQuery();
                    cmd.Connection.Close();
                }
            }
            catch (Exception x)
            {
                string err = x.Message;
                WriteLog(err);
                throw new InvalidOperationException(err);
            }
        }

    
    public void OutAccAmount(string PrivateID, decimal Amount, int CMID, int outType)
        {
            string sql = //"[dbo].[ZG_OutAccumAmount]";
            $"select FirstName+' '+Lastname from users where ID={CMID}";
            int step = 0;
            try
            {
                using (SqlCommand cmd = new SqlCommand(sql, new SqlConnection(CredDB())))
                {
                    cmd.Connection.Open();
                    step++;
                    string res = cmd.ExecuteScalar().ToString();
                    step++;
                    if (res.Length > 20) res = res.Substring(0, 20);
                    step++;
                    cmd.CommandText = $"insert into ZG_PayOperations([CustomerID],[OperationType],UID,[Amount],[PayTrnID]) select ID,{outType},{CMID},-{Amount},N'{res}' from customers where PrivateNumber='{PrivateID}'";
                    step++;
                    cmd.ExecuteNonQuery();
                    step++;
                    cmd.Connection.Close();
                    step++;
                }
            }
            catch (Exception x)
            { string err = $"step:{step}; MSG:{x.Message}";
                WriteLog(err);
                throw new InvalidOperationException(err);
                    }
        }
        #endregion

    }
    public class Result
    {
        public int Code { get; set; }
        public string Message { get; set; }
    }
}
