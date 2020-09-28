using System;

namespace ObjectPool.Pool.Exceptions
{
    public class PoolTimeOutException : Exception
    {
        public PoolTimeOutException(string message) : base(message)
        {
        }
    }
}