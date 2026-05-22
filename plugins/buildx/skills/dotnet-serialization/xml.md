# XML Serialization

`XmlSerializer` and `DataContractSerializer`. Load when generating/parsing XML with attributes, namespaces, schema constraints, or WCF-style data contracts.

## `XmlSerializer` (free-form XML)

Public fields/properties only; no constructor injection; requires a parameterless ctor.

```csharp
[XmlRoot("PurchaseOrder", Namespace = "http://www.cpandl.com")]
public class PurchaseOrder
{
    public Address ShipTo;
    public string OrderDate;
    [XmlArray("Items"), XmlArrayItem("OrderedItem")]
    public OrderedItem[] OrderedItems;
}
public class Address
{
    [XmlAttribute] public string Name;
    [XmlElement(IsNullable = false)] public string City;
}
```

Attributes: `[XmlRoot]`, `[XmlElement]`, `[XmlAttribute]`, `[XmlArray]`/`[XmlArrayItem]`, `[XmlIgnore]`, `[XmlText]`, `[XmlAnyAttribute]`/`[XmlAnyElement]`, `[XmlEnum]`, `[XmlChoiceIdentifier]`, `[XmlInclude(typeof(Derived))]` (polymorphism via `xsi:type`).

`XmlSerializer` emits dynamic IL on first use — pre-generate via `Microsoft.XmlSerializer.Generator` for cold-start-sensitive apps. AOT requires pre-generated serializers.

## `DataContractSerializer` (WCF / data contracts)

```csharp
[DataContract(Name = "Person", Namespace = "https://schemas.example.com/")]
public class Person
{
    [DataMember(Order = 0, IsRequired = true)] public string Name { get; set; } = "";
    [DataMember(Order = 1, EmitDefaultValue = false)] public int Age { get; set; }
    [IgnoreDataMember] public string? Internal { get; set; }
}
```

- Honors `[DataContract]`, `[DataMember]`, `[IgnoreDataMember]`, `[EnumMember]`, `[CollectionDataContract]`, `[KnownType]`.
- Polymorphism via `[KnownType(typeof(Derived))]`.
- Serializes private members (when `[DataMember]`); constructors **not** invoked on deserialize (`FormatterServices.GetUninitializedObject`).
- Variants: `DataContractSerializer` (XML), `DataContractJsonSerializer` (superseded by STJ). `NetDataContractSerializer` not supported in .NET 5+.

For high-throughput XML, prefer `XmlReader`/`XmlWriter` directly. `XmlSerializer` does **not** support open generic types or `Dictionary<TKey,TValue>` — wrap.
