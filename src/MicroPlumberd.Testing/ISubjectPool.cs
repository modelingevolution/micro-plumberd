public interface ISubjectPool
{
    Guid Store(string subject, Guid id);
    Guid A(string subject);
    Guid The(string subject);
    Guid GetOrCreate(string subject);
}