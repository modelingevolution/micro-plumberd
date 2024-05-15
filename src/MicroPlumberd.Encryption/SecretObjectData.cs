using System.Runtime.Serialization;

namespace MicroPlumberd.Encryption;

[DataContract]
internal record SecretObjectData
{
    public SecretObjectData(string Recipient, byte[] Data)
    {
        this.Recipient = Recipient;
        //this.Salt = Salt;
        this.Data = Data;
    }
    [DataMember(Order = 1)]
    public string Recipient { get; set; }
    //[DataMember(Order = 2)]
    //public byte[] Salt { get; set; }
    [DataMember(Order = 3)]
    public byte[] Data { get; set; }

}