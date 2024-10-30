using Microsoft.EntityFrameworkCore;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }

    //The following configures the application to use a Couchbase cluster
    //on localhost with a Bucket named "universities" and a Scope named "contoso"
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        using var loggingFactory = LoggerFactory.Create(builder => builder.AddConsole());
        options.UseCouchbase<INamedBucketProvider>(new ClusterOptions()
                .WithCredentials("Administrator", "password")
                .WithConnectionString("couchbase://localhost")
                .WithLogging(loggingFactory),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "Blogging";
                couchbaseDbContextOptions.Scope = "MyBlog";
            });
    }
}

public class Blog
{
    public string BlogId { get; set; }
    public string Url { get; set; }

    public List<Post> Posts { get; } = new();
}

public class Post
{
    public string PostId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }

    public int BlogId { get; set; }
    public Blog Blog { get; set; }
}