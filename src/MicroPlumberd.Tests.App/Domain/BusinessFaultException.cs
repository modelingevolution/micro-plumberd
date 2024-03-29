using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.App.Domain;

public class BusinessFaultException : FaultException<BusinessFault>
{
    public BusinessFaultException(string name) : base(new BusinessFault() { Name = name }) { }
}