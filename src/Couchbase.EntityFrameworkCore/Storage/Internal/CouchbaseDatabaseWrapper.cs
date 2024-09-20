using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Couchbase.Core.Exceptions;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.EntityFrameworkCore.Utilities;
using Newtonsoft.Json.Serialization;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper(DatabaseDependencies dependencies, ICouchbaseClientWrapper couchbaseClient)
    : Database(dependencies)
{
    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
       return Task.Run(async () => await SaveChangesAsync(entries).ConfigureAwait(false)).Result;
    }
    
    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new CancellationToken())
    {
        var updateCount = 0;
        foreach (var updateEntry in entries)
        {
            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;
            var primaryKey = entityType.GetPrimaryKey(entity);
            var scopeAndCollection = entityType.GetScopeAndCollection();
            var document= GenerateRootJson(updateEntry);
            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                    break;
                case EntityState.Unchanged:
                    break;
                case EntityState.Deleted:
                    if (await couchbaseClient.DeleteDocument(primaryKey, scopeAndCollection).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Modified:
                    if (await couchbaseClient.UpdateDocument(primaryKey, scopeAndCollection, document).ConfigureAwait(false))
                    {
                        updateCount++;
                    }
                    break;
                case EntityState.Added:
                {
                    if (await couchbaseClient.CreateDocument(primaryKey, scopeAndCollection, document).ConfigureAwait(false))
                    {
                        updateCount++;
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return updateCount;
    }
    
    
    private byte[] GenerateRootJson(IUpdateEntry updateEntry)
    {
        try
        {
            var entityType = updateEntry.EntityType;
            JsonWriterOptions writerOptions = new() { Indented = true };

            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream, writerOptions);
            writer.WriteStartObject();

            foreach (var property in entityType.GetProperties())
            {
                var jsonPropertyName = property.FindAnnotation("Relational:JsonPropertyName");
                var fieldName = jsonPropertyName?.Value?.ToString() ?? property.Name;
                var value = updateEntry.GetCurrentValue(property);
                var propertyType = GetUnderlyingType(property.ClrType);
                switch (propertyType.Name)
                {
                    case "String":
                        writer.WriteString(fieldName, (string)value);
                        break;
                    case "Int32":
                        writer.WriteNumber(fieldName, (int)value);
                        break;
                    case "DateTime":
                        writer.WriteString(fieldName, (DateTime)value);
                        break;
                    case "Decimal":
                        writer.WriteNumber(fieldName, (decimal)value);
                        break;
                    case "Byte[]":
                        writer.WriteBase64String(property.Name, new ReadOnlyMemory<byte>((byte[])value).Span);
                        break;
                    default:
                    {
                        if (propertyType.IsEnum)
                        {
                            writer.WriteString(property.Name, value != null ? value.ToString() : string.Empty);
                        }
                        else
                        {
                            throw new JsonException();
                        }
                        break;
                    }
                }
            }

            writer.WriteEndObject();
            writer.Flush();
            return stream.ToArray();
        }
        catch (Exception e)
        {
            
        }

        return null;
    }

    private Type? GetUnderlyingType(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return Nullable.GetUnderlyingType(type);
        }

        return type;
    }
}