using System;
using System.Collections.Generic;
using System.Linq;

namespace FactionTactics.Doctrine
{
    public sealed class DoctrinePackRegistry
    {
        private readonly List<IDoctrinePack> _packs;

        public DoctrinePackRegistry(IEnumerable<IDoctrinePack> packs)
        {
            _packs = packs.ToList();
        }

        public static DoctrinePackRegistry CreateDefault()
        {
            return new DoctrinePackRegistry(new IDoctrinePack[]
            {
                new RomanDoctrine(),
                new AmbushDoctrine(),
                new VikingShieldWallDoctrine(),
                new SteppeDoctrine(),
                new InsectSiegeDoctrine(),
                new CharredLegionDoctrine(),
                new PackHuntersDoctrine(),
                new ArtilleryJellyDoctrine(),
            });
        }

        public IReadOnlyList<IDoctrinePack> All => _packs;

        public IDoctrinePack? ResolveByPrefab(string prefabName)
        {
            foreach (var pack in _packs)
            {
                if (pack.IsEnabled && pack.MatchesPrefab(prefabName))
                    return pack;
            }
            return null;
        }

        public IDoctrinePack? GetById(string id)
        {
            return _packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
