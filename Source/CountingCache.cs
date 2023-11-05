﻿using System;
using System.Collections.Generic;

namespace ZombieLand
{
	public class CountingCache<K, V>
	{
		private readonly Dictionary<K, (V Value, int Count)> cache;
		private readonly int maxAccessCount;

		public CountingCache(int maxAccessCount)
		{
			this.maxAccessCount = maxAccessCount;
			cache = new Dictionary<K, (V, int)>();
		}

		public V Get(K key, Func<K, V> valueFetcher)
		{
			if (cache.TryGetValue(key, out var cacheEntry))
			{
				if (cacheEntry.Count < maxAccessCount)
				{
					cache[key] = (cacheEntry.Value, cacheEntry.Count + 1);
					return cacheEntry.Value;
				}
			}
			var value = valueFetcher(key);
			cache[key] = (value, 1);
			return value;
		}
	}
}