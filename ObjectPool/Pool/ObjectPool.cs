using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ObjectPool.Pool.Exceptions;
using ObjectPool.Pool.Models;

namespace ObjectPool.Pool
{
    public class ObjectPool<T> where T : class
    {
        private readonly ChannelReader<T> _idleObjectReader;
        private readonly ChannelWriter<T> _idleObjectWriter;
        private readonly int _maxPoolSize;
        private readonly int _minPoolSize;
        private readonly Func<T> _objectFactory;
        private readonly T[] _pooledObjects;
        private readonly ObjectPoolSettings _settings;
        private volatile int _idleCount;
        private volatile int _numObjects;

        public ObjectPool(ObjectPoolSettings settings, Func<T> objectFactory)
        {
            if (settings.MaxPoolSize < settings.MinPoolSize)
            {
                throw new ArgumentException(
                    $"Object can't have MaxPoolSize {settings.MaxPoolSize} under MinPoolSize {settings.MinPoolSize}");
            }

            _settings = settings;
            _objectFactory = objectFactory;

            var idleChannel = Channel.CreateUnbounded<T>();
            _idleObjectReader = idleChannel.Reader;
            _idleObjectWriter = idleChannel.Writer;

            _maxPoolSize = settings.MaxPoolSize;
            _minPoolSize = settings.MinPoolSize;

            _pooledObjects = new T[_maxPoolSize];
        }

        public PoolStatistics Statistics
        {
            get
            {
                var numObjects = _numObjects;
                var idleCount = _idleCount;
                return PoolStatistics.Create(numObjects, idleCount, numObjects - idleCount);
            }
        }

        private async ValueTask<T> RentAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var pooledObject = CreateNewPooledObject();
                if (pooledObject != null)
                {
                    return pooledObject;
                }

                using var timeoutSource = new CancellationTokenSource(_settings.TimeOut);
                var timeoutToken = timeoutSource.Token;
                try
                {
                    await using var _ = cancellationToken.Register(cts => ((CancellationTokenSource) cts!).Cancel(),
                        timeoutSource);

                    pooledObject = await _idleObjectReader.ReadAsync(timeoutToken);
                    if (CheckIdleConnector(pooledObject))
                    {
                        return pooledObject;
                    }

                    if (TryGetIdlePooledObject(out pooledObject))
                    {
                        return pooledObject;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Debug.Assert(timeoutToken.IsCancellationRequested);
                    throw new PoolTimeOutException(
                        $"The object pool has been exhausted, either raise MaxPoolSize (currently {_maxPoolSize}) " +
                        $"or Timeout (currently {_settings.TimeOut} seconds)");
                }
            }
        }

        public ValueTask<T> Rent(CancellationToken cancellationToken)
            => TryGetIdlePooledObject(out var pooledObject)
                ? new ValueTask<T>(pooledObject)
                : RentAsync(cancellationToken);

        public void Return(T pooledObject)
        {
            Interlocked.Increment(ref _idleCount);
            var written = _idleObjectWriter.TryWrite(pooledObject);
            Debug.Assert(written);
        }

        private T CreateNewPooledObject()
        {
            for (var numObjects = _numObjects; numObjects < _maxPoolSize; numObjects = _numObjects)
            {
                if (Interlocked.CompareExchange(ref _numObjects, numObjects + 1, numObjects) != numObjects)
                {
                    continue;
                }

                try
                {
                    var pooledObject = _objectFactory();

                    var i = 0;
                    for (; i < _maxPoolSize; i++)
                    {
                        if (Interlocked.CompareExchange(ref _pooledObjects[i], pooledObject, null) == null)
                        {
                            break;
                        }
                    }

                    if (i == _maxPoolSize)
                    {
                        throw new Exception(
                            $"Could not find free slot in {_numObjects} when opening. Please report a bug.");
                    }

                    return pooledObject;
                }
                catch
                {
                    Interlocked.Decrement(ref _numObjects);
                    _idleObjectWriter.TryWrite(null);
                    throw;
                }
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGetIdlePooledObject([NotNullWhen(true)] out T pooledObject)
        {
            while (_idleObjectReader.TryRead(out var nullablePooledObject))
            {
                if (!CheckIdleConnector(nullablePooledObject))
                {
                    continue;
                }

                pooledObject = nullablePooledObject;
                return true;
            }

            pooledObject = null;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CheckIdleConnector([NotNullWhen(true)] T connector)
        {
            if (connector is null)
            {
                return false;
            }

            Interlocked.Decrement(ref _idleCount);
            return true;
        }
    }
}