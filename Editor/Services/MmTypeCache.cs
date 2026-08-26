using System;
using System.Collections.Generic;

namespace Dennokoworks.MeshModularizer
{
    /// <summary>
    /// アセンブリを走査して型を名前引きし、結果をキャッシュする。
    /// Modular Avatar や VRCSDK への静的参照を持たずに連携するために使用する。
    /// </summary>
    public static class MmTypeCache
    {
        private static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>();

        public static Type Find(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (Cache.TryGetValue(typeName, out var cached)) return cached;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    Cache[typeName] = type;
                    return type;
                }
            }

            Cache[typeName] = null;
            return null;
        }

        public static Type FindAny(IReadOnlyList<string> candidates)
        {
            if (candidates == null) return null;
            for (int i = 0; i < candidates.Count; i++)
            {
                var type = Find(candidates[i]);
                if (type != null) return type;
            }
            return null;
        }
    }
}
