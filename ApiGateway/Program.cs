var builder = WebApplication.CreateBuilder(args);

// Load the YARP configuration from appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Enable the gateway middleware
// This will route incoming requests to the appropriate downstream services based on the YARP configuration
// For example, if you have a route configured for "/api/tickets", YARP will forward requests to that path to the corresponding service
// You can also add additional middleware here if needed (e.g., authentication, logging, etc.)
// What is reverse proxy? A reverse proxy is a server that sits in front of one or more backend servers and forwards client requests to those servers. It acts as an intermediary for requests from clients seeking resources from the backend servers. In the context of an API Gateway, a reverse proxy can be used to route requests to different microservices based on the request path, handle load balancing, and provide additional features like authentication and logging.
// In this code, we are using YARP (Yet Another Reverse Proxy) to set up a reverse proxy in our API Gateway. The configuration for the routes and clusters (backend services) is loaded from the appsettings.json file under the "ReverseProxy" section. When a request comes in, YARP will look at the configuration to determine which backend service to forward the request to based on the request path and other criteria defined in the configuration.
// What is YARP? YARP (Yet Another Reverse Proxy) is a reverse proxy library for .NET that allows you to easily create a reverse proxy server. It provides features like routing, load balancing, and middleware support, making it a powerful tool for building API Gateways and other types of reverse proxies in .NET applications. With YARP, you can define routes and clusters in your configuration, and it will handle the routing of requests to the appropriate backend services based on that configuration.
// What is API Gateway and is that the reverse proxy? Think of it like a square is a rectangle but a rectangle is not a square rule. API Gateway is built on top of a reverse proxy. So rectangle = Reverse Proxy, square = API Gateway. An API Gateway is a Reverse Proxy that has been upgraded with application-level intelligence. Instead of just blindly forwarding traffic, it intercepts the request and applies cross-cutting business and security rules before the microservices ever see it. For example, an API Gateway can handle authentication, rate limiting, logging, and other concerns that are common across multiple microservices. It can also perform request transformation, such as modifying headers or request bodies before forwarding them to the backend services. In this code, we are using YARP to set up a reverse proxy in our API Gateway, which allows us to route requests to different microservices based on the configuration defined in appsettings.json. The API Gateway can also be extended with additional middleware to handle other concerns as needed.
app.MapReverseProxy();

app.Run();