using System;
using System.Collections;
using System.Collections.Generic;

namespace SocksNet
{
    public class BiDictionary<TFirst, TSecond> : IEnumerable<KeyValuePair<TFirst, TSecond>> 
        where TFirst : notnull
        where TSecond : notnull
    {
        private IDictionary<TFirst, TSecond> _firstToSecond = new Dictionary<TFirst, TSecond>();
        private IDictionary<TSecond, TFirst> _secondToFirst = new Dictionary<TSecond, TFirst>();

        public void Add(TFirst first, TSecond second)
        {
            _firstToSecond.Add(first, second);
            _secondToFirst.Add(second, first);
        }

        // Note potential ambiguity using indexers (e.g. mapping from int to int)
        // Hence the methods as well...
        public TSecond this[TFirst first]
        {
            get
            {
                return TryGetByFirst(first, out TSecond second) ? second : throw new Exception("No value in dictionary."); 
            }
        }

        public TFirst this[TSecond second]
        {
            get
            {
                return TryGetBySecond(second, out TFirst first) ? first : throw new Exception("No value in dictionary."); 
            }
        }

        public bool TryGetByFirst(TFirst first, out TSecond second)
        {
            return _firstToSecond.TryGetValue(first, out second);
        }

        public bool TryGetBySecond(TSecond second, out TFirst first)
        {
            return _secondToFirst.TryGetValue(second, out first);
        }

        public IEnumerator<KeyValuePair<TFirst, TSecond>> GetEnumerator() => _firstToSecond.GetEnumerator();        
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}