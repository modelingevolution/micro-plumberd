using ModelingEvolution.DirectConnect;

namespace MicroPlumberd.Tests.AppSrc;

public class BusinessFaultException : CommandFaultException<BusinessFault>
{
    public BusinessFaultException(string name) : base(new BusinessFault() { Name = name }) { }
}