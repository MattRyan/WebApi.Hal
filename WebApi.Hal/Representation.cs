using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using WebApi.Hal.Interfaces;
using WebApi.Hal.JsonConverters;

namespace WebApi.Hal
{
    public abstract class Representation : IResource
    {
        protected Representation()
        {
            Links = new List<Link>();
        }

        [JsonProperty("_embedded")]
        private ILookup<string, IResource> Embedded { get; set; }

        [JsonIgnore]
        readonly IDictionary<PropertyInfo, object> embeddedResourceProperties = new Dictionary<PropertyInfo, object>();
        
        [OnSerializing]
        private void OnSerialize(StreamingContext context)
        {
            // Clear the embeddedResourceProperties in order to make this object re-serializable.
            embeddedResourceProperties.Clear();

            RepopulateHyperMedia();

            if (ResourceConverter.IsResourceConverterContext(context))
            {
                // put all embedded resources and lists of resources into Embedded for the _embedded serializer
                var resourceList = new List<Tuple<string, IResource>>();
                foreach (var prop in GetType().GetProperties().Where(p => IsEmbeddedResourceType(p.PropertyType)))
                {
                    var val = prop.GetValue(this, null);

                    if (val != null)
                    {
                        var jsonPropertyAttribute = prop.GetCustomAttribute<JsonPropertyAttribute>();
                        // remember embedded resource property for restoring after serialization
                        embeddedResourceProperties.Add(prop, val);
                        // add embedded resource to collection for the serializtion
                        var resources = val as IEnumerable<IResource>;
                        if (resources != null)
                        {
                            resourceList.AddRange(from resource in resources
                                                  let rel = GetRel(jsonPropertyAttribute, resource)
                                                  select new Tuple<string, IResource>(rel, resource));
                        }
                        else
                        {
                            var resource = (IResource) val;
                            var rel = GetRel(jsonPropertyAttribute, resource);
                            resourceList.Add(new Tuple<string, IResource>(rel, resource));
                        }
                        // null out the embedded property so it doesn't serialize separately as a property
                        prop.SetValue(this, null, null);
                    }
                }
                Embedded = resourceList.Count > 0
                               ? resourceList.ToLookup(r => r.Item1, r => r.Item2)
                               : null;
            }
        }

        public static string GetRel(JsonPropertyAttribute jsonPropertyAttribute, 
                                    IResource resource)
        {
            if (jsonPropertyAttribute != null)
            {
                return jsonPropertyAttribute.PropertyName;
            }
            if (string.IsNullOrEmpty(resource.Rel))
            {
                return "unknownRel-" + resource.GetType().Name;
            }
            return resource.Rel;
        }

        [OnSerialized]
        private void OnSerialized(StreamingContext context)
        {
            if (ResourceConverter.IsResourceConverterContext(context))
            {
                // restore embedded resource properties
                foreach (var prop in embeddedResourceProperties.Keys)
                    prop.SetValue(this, embeddedResourceProperties[prop], null);
            }
        }

        internal static bool IsEmbeddedResourceType(Type type)
        {
            return typeof (IResource).IsAssignableFrom(type) ||
                   typeof (IEnumerable<IResource>).IsAssignableFrom(type);
        }

        public void RepopulateHyperMedia()
        {
            CreateHypermedia();
            if (Links.Count(l=>l.Rel == "self") == 0)
                Links.Insert(0, new Link { Rel = "self", Href = Href });
        }

        [JsonIgnore]
        public virtual string Rel { get; set; }

        [JsonIgnore]
        public virtual string Href { get; set; }

        [JsonIgnore]
        public string LinkName { get; set; }

        public IList<Link> Links { get; set; }

        protected internal abstract void CreateHypermedia();
    }
}
