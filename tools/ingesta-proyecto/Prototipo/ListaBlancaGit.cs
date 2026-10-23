namespace IngestaProyecto.Prototipo;

/// <summary>
/// Un origen Git admitido: esquema, host y puerto exactos, y opcionalmente un prefijo de ruta (la organizacion de Azure
/// DevOps: <c>https://dev.azure.com/&lt;org&gt;/</c>). No hay comodines ni sufijos de dominio: un comodin es lo que deja
/// pasar <c>dev.azure.com.atacante.example</c>.
/// </summary>
public sealed record OrigenGitPermitido(string Esquema, string Host, int Puerto, string? PrefijoRuta = null);

/// <summary>
/// Validacion de la URL antes de tocar la red. La mitigacion de SSRF es que el servicio solo habla con hosts de esta
/// lista: una IP interna, un metadata endpoint o un puerto de Qdrant no estan en ella y se rechazan sin resolver DNS.
/// Lo que la lista no cubre (redirecciones del host permitido hacia otro sitio) lo corta el clonador con
/// <c>http.followRedirects=false</c>.
/// </summary>
public static class ListaBlancaGit
{
    public const int MaxCaracteresUrl = 2_048;

    public static (Uri? Uri, string? Error) Validar(string? url, IReadOnlyList<OrigenGitPermitido> permitidos)
    {
        if (string.IsNullOrWhiteSpace(url))
            return (null, "La URL es obligatoria.");
        if (url.Length > MaxCaracteresUrl)
            return (null, $"La URL admite como maximo {MaxCaracteresUrl} caracteres.");
        if (url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            return (null, "La URL no puede llevar espacios ni caracteres de control.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (null, "No es una URL absoluta.");
        if (uri.Scheme is not ("https" or "http"))
            return (null, $"Esquema '{uri.Scheme}' no admitido: solo https.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return (null, "La URL no puede llevar usuario ni contrasena: el servicio usa su propia identidad.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return (null, "La URL no puede llevar query ni fragmento.");

        // Uri ya resuelve "/a/../b"; un punto o una barra codificados los resolveria el servidor despues del chequeo
        // de prefijo, asi que no se aceptan. Otros escapes (%20 en nombres de proyecto de ADO) si.
        var crudo = uri.OriginalString;
        foreach (var codificado in new[] { "%2e", "%2f", "%5c", "%00" })
            if (crudo.Contains(codificado, StringComparison.OrdinalIgnoreCase))
                return (null, "La URL no puede llevar puntos, barras ni nulos codificados.");

        var host = uri.IdnHost;
        var permitido = permitidos.FirstOrDefault(p =>
            string.Equals(p.Esquema, uri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase)
            && p.Puerto == uri.Port
            && (p.PrefijoRuta is null || uri.AbsolutePath.StartsWith(p.PrefijoRuta, StringComparison.OrdinalIgnoreCase)));

        return permitido is null
            ? (null, $"El origen '{uri.Scheme}://{host}:{uri.Port}{uri.AbsolutePath}' no esta en la lista de origenes Git permitidos.")
            : (uri, null);
    }
}
