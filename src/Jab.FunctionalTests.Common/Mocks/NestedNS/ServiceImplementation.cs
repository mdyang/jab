using Jab;

namespace JabTests.NestedNS
{
    internal class ServiceImplementation : IService
    {
    }

    internal class ServiceImplementation<T> : IService<T>
    {
        public ServiceImplementation(T innerService)
        {
            InnerService = innerService;
        }

        public T InnerService { get; }
    }
}