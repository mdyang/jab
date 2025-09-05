using Jab;

namespace JabTests.NestedNS
{
    internal interface IService
    {
    }
    internal interface IService<T>
    {
        T InnerService { get; }
    }
}