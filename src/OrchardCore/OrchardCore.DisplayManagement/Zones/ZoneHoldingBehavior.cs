using System;
using System.Linq;
using System.Threading.Tasks;
using OrchardCore.DisplayManagement.Shapes;

namespace OrchardCore.DisplayManagement.Zones
{
    public interface IZoneHolding : IShape
    {
        Zones Zones { get; }
    }

    /// <summary>
    /// Provides the behavior of shapes that have a Zones property.
    /// Examples include Layout and Item
    ///
    /// * Returns a fake parent object for zones
    /// Foo.Zones
    ///
    /// *
    /// Foo.Zones.Alpha :
    /// Foo.Zones["Alpha"]
    /// Foo.Alpha :same
    ///
    /// </summary>
    public class ZoneHolding : Shape, IZoneHolding
    {
        private readonly Func<ValueTask<IShape>> _zoneFactory;

        public ZoneHolding(Func<ValueTask<IShape>> zoneFactory)
        {
            _zoneFactory = zoneFactory;
        }

        private Zones _zones;

        public Zones Zones => _zones ??= new Zones(_zoneFactory, this);

        public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object result)
        {
            var name = binder.Name;

            if (!base.TryGetMember(binder, out result) || (null == result))
            {
                // substitute nil results with a robot that turns adds a zone on
                // the parent when .Add is invoked
                result = new ZoneOnDemand(_zoneFactory, this, name);
                TrySetMemberImpl(name, result);
            }

            return true;
        }
    }

    /// <remarks>
    /// Returns a ZoneOnDemand object the first time the indexer is invoked.
    /// If an item is added to the ZoneOnDemand then the zoneFactory is invoked to create a zone shape and this item is added to the zone.
    /// Then the zone shape is assigned in palce of the ZoneOnDemand. A ZoneOnDemand returns true when compared to null such that we can
    /// do Zones["Foo"] == null to see if anything has been added to a zone, without instantiating a zone when accessing the indexer.
    /// </remarks>
    public class Zones : Composite
    {
        private readonly Func<ValueTask<IShape>> _zoneFactory;
        private readonly ZoneHolding _parent;

        public bool IsNotEmpty(string name) => !(this[name] is ZoneOnDemand);

        public Zones(Func<ValueTask<IShape>> zoneFactory, ZoneHolding parent)
        {
            _zoneFactory = zoneFactory;
            _parent = parent;
        }

        public IShape this[string name]
        {
            get
            {
                TryGetMemberImpl(name, out var result);
                return result as IShape;
            }

            set
            {
                TrySetMemberImpl(name, value);
            }
        }

        public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object result)
        {
            return TryGetMemberImpl(binder.Name, out result);
        }

        protected override bool TryGetMemberImpl(string name, out object result)
        {
            if (!_parent.Properties.TryGetValue(name, out result))
            {
                result = new ZoneOnDemand(_zoneFactory, _parent, name);
            }

            return true;
        }

        protected override bool TrySetMemberImpl(string name, object value)
        {
            _parent.Properties[name] = value;
            return true;
        }
    }

    /// <remarks>
    /// InterfaceProxyBehavior()
    /// NilBehavior() => return Nil on GetMember and GetIndex in all cases
    /// ZoneOnDemandBehavior(_zoneFactory, _parent, name)  => when a zone (Shape) is
    /// created, replace itself with the zone so that Layout.ZoneName is no more equal to Nil
    /// </remarks>
    public class ZoneOnDemand : Shape
    {
        private readonly Func<ValueTask<IShape>> _zoneFactory;
        private readonly ZoneHolding _parent;
        private readonly string _potentialZoneName;

        public ZoneOnDemand(Func<ValueTask<IShape>> zoneFactory, ZoneHolding parent, string potentialZoneName)
        {
            _zoneFactory = zoneFactory;
            _parent = parent;
            _potentialZoneName = potentialZoneName;
        }

        public override bool TryGetMember(System.Dynamic.GetMemberBinder binder, out object result)
        {
            // NilBehavior
            result = Nil.Instance;
            return true;
        }

        public override bool TryGetIndex(System.Dynamic.GetIndexBinder binder, object[] indexes, out object result)
        {
            // NilBehavior
            result = Nil.Instance;
            return true;
        }

        public override bool TryInvokeMember(System.Dynamic.InvokeMemberBinder binder, object[] args, out object result)
        {
            var name = binder.Name;

            // NilBehavior
            if (!args.Any() && name != "ToString")
            {
                result = Nil.Instance;
                return true;
            }

            return base.TryInvokeMember(binder, args, out result);
        }

        public override string ToString()
        {
            return String.Empty;
        }

        public override bool TryConvert(System.Dynamic.ConvertBinder binder, out object result)
        {
            if (binder.ReturnType == typeof(string))
            {
                result = null;
            }
            else if (binder.ReturnType.IsValueType)
            {
                result = Activator.CreateInstance(binder.ReturnType);
            }
            else
            {
                result = null;
            }

            return true;
        }

        public static bool operator ==(ZoneOnDemand a, object b)
        {
            // if ZoneOnDemand is compared to null it must return true
            return b == null || ReferenceEquals(b, Nil.Instance);
        }

        public static bool operator !=(ZoneOnDemand a, object b)
        {
            // if ZoneOnDemand is compared to null it must return true
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return true;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_parent != null ? _parent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (_potentialZoneName != null ? _potentialZoneName.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override async ValueTask<IShape> AddAsync(object item, string position)
        {
            if (item == null)
            {
                return _parent;
            }

            var zone = await _zoneFactory();
            zone.Properties["Parent"] = _parent;
            zone.Properties["ZoneName"] = _potentialZoneName;
            _parent.Properties[_potentialZoneName] = zone;

            return await zone.AddAsync(item, position);
        }
    }
}
