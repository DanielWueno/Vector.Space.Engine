namespace Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Reyma.VectorSpace.Engine.Microservice.Business;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Arma la cadena de conexion segun el entorno (H12): en Development usa ConnectionStrings:Default
/// (o las partes DatabaseConnection si se definen), y unicamente CONNECTION_STRING (leida de
/// IConfiguration, DD7) en cualquier otro entorno.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class ConnectionStringExtensions
{
    private const string ConnectionStringKey = "CONNECTION_STRING";
    private const string DatabaseConnectionSection = "DatabaseConnection";

    internal static string GetReymaConnectionString(this IConfiguration config, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            var developmentConnectionString = BuildDevelopmentConnectionString(config);
            config["ConnectionStrings:Default"] = developmentConnectionString;
            return developmentConnectionString;
        }

        var connectionString = config[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Falta la configuracion requerida: '{ConnectionStringKey}'.");

        return connectionString;
    }

    private static string BuildDevelopmentConnectionString(IConfiguration config)
    {
        var section = config.GetSection(DatabaseConnectionSection);

        var host = section["Host"];
        var database = section["Database"];
        var userId = section["UserId"];
        var password = section["Password"];

        // Sin partes DatabaseConnection se usa ConnectionStrings:Default de appsettings.Development.json;
        // con cualquier parte (variables de entorno, user-secrets) la cadena se arma por partes y deben venir todas.
        if (string.IsNullOrEmpty(host) && string.IsNullOrEmpty(database)
            && string.IsNullOrEmpty(userId) && string.IsNullOrEmpty(password))
        {
            var configured = config.GetConnectionString("Default");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(host))
            missing.Add($"{DatabaseConnectionSection}:Host");
        if (string.IsNullOrWhiteSpace(database))
            missing.Add($"{DatabaseConnectionSection}:Database");
        if (string.IsNullOrWhiteSpace(userId))
            missing.Add($"{DatabaseConnectionSection}:UserId");
        if (string.IsNullOrWhiteSpace(password))
            missing.Add($"{DatabaseConnectionSection}:Password");

        if (missing.Count > 0)
            throw new InvalidOperationException($"Falta la configuracion requerida: {string.Join(", ", missing)}.");

        var port = section.GetValue<int?>("Port") ?? 5432;
        var pooling = section.GetValue<bool?>("Pooling") ?? true;
        var applicationName = section["ApplicationName"];

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = userId,
            Password = password,
            Pooling = pooling,
            ApplicationName = string.IsNullOrWhiteSpace(applicationName) ? ServicioInfo.Nombre : applicationName
        };

        return builder.ConnectionString;
    }
}
