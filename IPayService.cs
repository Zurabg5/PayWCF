using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace PayWCF
{
    // NOTE: You can use the "Rename" command on the "Refactor" menu to change the interface name "IService1" in both code and config file together.
    [ServiceContract (Namespace = "https://services.intelexpress.ge")]
    public interface IPayService
    {

        #region  TBCPay
        [OperationContract]
        List<LoanTypes> GetLoanTypes();

        [OperationContract]
        LoanList GetLoans(int TypeID, string PrivateID, out string info);

        [OperationContract]
        string GetLoanFullAmounts(int LoanID);


        [OperationContract]
        Result PayAmount(int LoanID, decimal Amount, int PayType, string Terminal, string PrivateID,int Period,string TransactionID);


        [OperationContract]
        string GetPayData(int LoanID, int Period);

        #endregion
        #region i-Banking
        [OperationContract]
        string GetPayDataFromID(string PrivateID, int ID);
        [OperationContract]
        string GetFullAmountFromID(string PrivateID, int ID);
        [OperationContract]
        Result PayAmountFromID(int ID, decimal Amount, int PayType, string PrivateID, string TransactionID);

        #endregion
        #region Reporting
        [OperationContract]
        List<PayRecord> GetPayHistory(DateTime DT1, DateTime DT2);

        [OperationContract]
        List<ReportRecord> GetPayReport(DateTime DT1, DateTime DT2);
        [OperationContract]
        List<PaySumRecord> GetPaySum(DateTime DT1, DateTime DT2);
        [OperationContract]
        string getPayConvertation(DateTime DT1, DateTime DT2);
        [OperationContract]
        List<Nashti> getNashti(DateTime DT);
        [OperationContract]
        int? getCMID(int MTID);
        [OperationContract]
        void OutAccAmount(string PrivateID, decimal Amount, int CMID, int outType);
        [OperationContract]
        void OutAccAmountLoan(string PrivateID, decimal Amount, int CMID,long LoanID,long ExternalID, int outType);
        

        [OperationContract]
        decimal GetAcumulateAmount(string PrivateID);
        [OperationContract]
        long? getClientLoanIdFromExternalID(string Pid, long ID);
        #endregion
    }


    // Use a data contract as illustrated in the sample below to add composite types to service operations.


    public class LoanList
    {
        public string Client { get; set; }
        public decimal AcumulateAmount { get; set; }
        public List<Loan> Loans { get; set; }
    }
    public class Loan
    {
        public int ID { get; set; }
        public int LoanID { get; set; }
        public string Currency { get; set; }
        public DateTime CreateDate { get; set; }
        public DateTime? NextPayDate { get; set; }
        public decimal LoanAmount { get; set; }

    }
    public class LoanTypes
    {
        public int ID { get; set; }
        public string Name { get; set; }
    }
    public class PayRecord
    {
        public int ID { get; set; }
        public DateTime DTTM { get; set; }
        public string PrivateID { get; set; }
        public string Client { get; set; }
        public int LoanID { get; set; }
        public int ExternalID { get; set; }
        public string Terminal { get; set; }
        public int PayType { get; set; }
        public string PayTypeName { get; set; }
        public Int64 TransactionID { get; set; }
        public decimal LoanAmount { get; set; }
        public decimal PayCore { get; set; }
        public decimal PayPenalty { get; set; }
        public decimal PayPercent { get; set; }
        public decimal InAmount { get; set; }
        public decimal Acumulate { get; set; }
        public int NewStatusID { get; set; }
        public string StatusName { get; set; }
    }
public class ReportRecord
    {
        public int ID { get; set; }
        public DateTime DT { get; set; }
        public long CustomerID { get; set; }
        public string PrivateID { get; set; }
        public string FullName { get; set; }
        public int OperationType { get; set; }
        public decimal Amount { get; set; }
        public string PayTrnID { get; set; }
        public long? TransactionID { get; set; }
        public int? LoanID { get; set; }
        public long? ExternalID { get; set; }
        public decimal accAmount { get; set; }
        public decimal InAmount { get; set; }
        public decimal LoanAmount { get; set; }
        public decimal GEL { get; set; }
        public decimal NewAcc { get; set; }
        public decimal? otherOut { get; set; }
        public int PayType { get; set; }
        public string PayOperation { get; set; }
        public string Terminal { get; set; }
        public double Rate { get; set; }
        public string Currency { get; set; }
        public string statusName { get; set; }
    }
public class PaySumRecord
    {
        public DateTime DT { get; set; }
        public string CCY { get; set; }
        public decimal? inAmount { get; set; }
        public decimal? OutAmount { get; set; }
        public decimal? OutGel { get; set; }
    }
    public class Nashti
    {
        public string FullName { get; set; }
        public string PrivateID { get; set; }
        public decimal Amount { get; set; }
    }
    public class Operator
    {
        public int UserID { get; set; }
        public int Menuenable { get; set; }
        public string Name { get; set; }
    }
}
