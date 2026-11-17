"""1.23: simulacion fuera de linea de la expansion por cercania en el archivo (protocolo fijado antes de correr:
docs/evaluaciones/1.23/protocolo.md).

Sobre el top-10 denso de c1024-w8192 ya guardado (1.11) reproduce el ensamblador del cliente
(GenerationContextAssembler.BuildContextBlock: 12 000 caracteres, se omite entero lo que no cabe y se sigue) y lo
compara con tres variantes de expansion (vecinos-1, clase, seccion-md) que agregan chunks del mismo archivo a cada hit
y admiten el grupo de forma atomica. Mide ancla-en-contexto@12k con la regla de acierto de harness.evaluar_item.

Sin Qdrant, sin embeddings, sin LLM. No escribe contenido de los repos fuente: solo IDs, conteos y booleanos.

Uso:  uv run --system-certs --no-project --python 3.12 --with tokenizers python expansion.py
"""
import hashlib
import json
import os
import pathlib
import re
import sys

RAIZ = pathlib.Path(__file__).resolve().parents[2]
CACHE = RAIZ / ".cache" / "embeddings-eval"
RESULTADO_111 = RAIZ / "docs" / "evaluaciones" / "1.11" / "resultados" / "c1024-w8192.json"
SALIDA = RAIZ / "docs" / "evaluaciones" / "1.23" / "resultados"
RAG = pathlib.Path(os.environ.get("RAG_CONTEXT_ENGINE", r"D:\Git\Rag.Context.Engine"))
FUENTES = pathlib.Path(os.environ.get("VSE_FUENTES", r"D:\Git"))
TOKENIZADOR = RAIZ / ".cache" / "hf" / "models--Snowflake--snowflake-arctic-embed-m-v2.0" / "snapshots"

COLECCIONES = {
    "bsuite-auditorias-test": "bsuite-auditorias.eval-set.json",
    "innovapp-docs": "innovapp-docs.eval-set.json",
    "micro-repo": "tickets-microservice.eval-set.json",
}
RAICES = {  # las de ingestar-control.ps1
    "bsuite-auditorias-test": [FUENTES / "BusinessSuite.Xaf" / "src" / "Modules" / "REYMA.XAFR1PV.Compras" / "Auditorias",
                               FUENTES / "BusinessSuite.Xaf" / "src" / "Modules" / "REYMA.XAFR1PV.Compras" / "Compras" / "Utils",
                               FUENTES / "BusinessSuite.Xaf" / "src" / "Modules" / "REYMA.XAFR1PV.Base"],
    "innovapp-docs": [FUENTES / "docs-bsute-innovapp-plan"],
    "micro-repo": [FUENTES / "Reyma.TI.Tickets.Microservice"],
}

MAX_CONTEXTO = 12_000          # GenerationContextAssembler.MaxContextCharacters
SEPARADOR = "\n\n---\n\n"      # GenerationContextAssembler.ChunkSeparator
TOPE_GRUPO = 4                 # vecinos maximos en 'clase' y 'seccion-md' (protocolo)
VARIANTES = ("control", "vecinos-1", "clase", "seccion-md")

RE_CLASE = re.compile(r"^// (?:class|interface|record|struct|enum): ([^:\n]+)", re.M)
RE_METODO = re.compile(r"^// method: ([^(\n]+)\(", re.M)
RE_NS = re.compile(r"^// Namespace: (.+)$", re.M)
RE_REPO = re.compile(r"^// Repository: (.+)$", re.M)
RE_SECCION = re.compile(r"^\[Secci[oó]n: (.+)\]$", re.M)
RE_FRAGMENTO = re.compile(r"^// \[Fragment \d+\][^\n]*\n")


def leer_jsonl(ruta):
    with open(ruta, encoding="utf-8") as f:
        return [json.loads(l) for l in f]


def texto_fuente(col, rel, cache):
    clave = (col, rel)
    if clave not in cache:
        cache[clave] = None
        for raiz in RAICES[col]:
            p = raiz / rel.replace("\\", "/")
            if p.is_file():
                t = p.read_bytes().decode("utf-8-sig", errors="replace")
                cache[clave] = t.replace("\r\n", "\n").replace("\r", "\n")
                break
    return cache[clave]


RE_ETIQUETA_SECCION = re.compile(r"^\[Secci[oó]n: [^\n]*\]\n+")


def indice_de_lineas(texto):
    lineas = [l.strip() for l in texto.split("\n")]
    por_linea = {}
    for k, l in enumerate(lineas):
        if l:
            por_linea.setdefault(l, []).append(k)
    return lineas, por_linea


def ubicar_por_lineas(contenido, lineas_archivo, por_linea):
    """Enmienda del instrumento (resultado.md): ancla en la linea mas distintiva del chunk presente en el archivo y
    elige la aparicion que mas lineas del chunk explica en su ventana. Devuelve (inicio, fin) 1-based o None."""
    propias = contenido.split("\n")
    candidatas = [(len(por_linea[l.strip()]), -len(l.strip()), off) for off, l in enumerate(propias)
                  if l.strip() and l.strip() in por_linea]
    if not candidatas:
        return None
    _, _, off = min(candidatas)
    ancla = propias[off].strip()
    utiles = {l.strip() for l in propias if l.strip()}
    mejor = None
    for pos in por_linea[ancla]:
        inicio = max(0, pos - off)
        ventana = set(lineas_archivo[inicio:inicio + len(propias) + 5])
        puntos = len(utiles & ventana)
        if mejor is None or puntos > mejor[0]:
            mejor = (puntos, inicio)
    if mejor[0] * 2 < len(utiles):  # menos de la mitad de sus lineas: no se considera ubicado
        return None
    return mejor[1] + 1, mejor[1] + len(propias)


def metadatos(col, chunks):
    """Linea, clase, metodo/seccion y namespace de cada chunk (busqueda literal; si falla, anclaje por lineas)."""
    textos, indices = {}, {}
    estado = {"literal": 0, "por_lineas": 0, "sin_archivo": 0, "sin_ubicar": 0}
    for c in chunks:
        rel = c["relative_path"] or ""
        enr = c["enriched_content"] or ""
        ext = os.path.splitext(rel)[1].lower()
        c["_lenguaje"] = "CSharp" if ext == ".cs" else ("Markdown" if ext == ".md" else "Otro")
        c["_clase"] = (m.group(1).strip() if (m := RE_CLASE.search(enr)) else None)
        c["_metodo"] = (m.group(1).strip() if (m := RE_METODO.search(enr)) else None)
        c["_seccion"] = (m.group(1).strip() if (m := RE_SECCION.search(enr.split("\n", 1)[0] + "\n")) else None)
        c["_ns"] = (m.group(1).strip() if (m := RE_NS.search(enr)) else None)
        c["_repo"] = (m.group(1).strip() if (m := RE_REPO.search(enr)) else "my-repo")
        c["_linea"] = c["_fin"] = None
        texto = texto_fuente(col, rel, textos)
        if texto is None:
            estado["sin_archivo"] += 1
            continue
        # El chunk Markdown lleva su etiqueta de seccion como primera linea de content; el fragmento, su marca.
        contenido = RE_ETIQUETA_SECCION.sub("", c["content"].replace("\r\n", "\n"), count=1)
        contenido = RE_FRAGMENTO.sub("", contenido, count=1).strip("\n")
        idx = texto.find(contenido) if contenido.strip() else -1
        if idx >= 0:
            c["_linea"] = texto.count("\n", 0, idx) + 1
            c["_fin"] = c["_linea"] + contenido.count("\n")
            estado["literal"] += 1
            continue
        if rel not in indices:
            indices[rel] = indice_de_lineas(texto)
        lugar = ubicar_por_lineas(contenido, *indices[rel])
        if lugar is None:
            estado["sin_ubicar"] += 1
            continue
        c["_linea"], c["_fin"] = lugar
        estado["por_lineas"] += 1
    return estado


def cabecera(c, rango):
    """BuildChunkHeader (score de ancho fijo: el resultado de 1.11 no guarda el coseno)."""
    lineas = [f"[Chunk #{rango} | Score: 0.00]", f"Repository : {c['_repo']}",
              f"File       : {c['relative_path']} (lines {c['_linea'] or 0}–{c['_fin'] or 0})"]
    if c["_ns"]:
        lineas.append(f"Namespace  : {c['_ns']}")
    if c["_clase"]:
        lineas.append(f"Class      : {c['_clase']}")
    metodo = c["_seccion"] if c["_lenguaje"] == "Markdown" else c["_metodo"]
    if metodo:
        lineas.append(f"{'Section' if c['_lenguaje'] == 'Markdown' else 'Method':<11}: {metodo}")
    return "\n".join(lineas)


def indices_por_archivo(chunks):
    por = {}
    for i, c in enumerate(chunks):
        if c["_linea"] is not None:
            por.setdefault(c["relative_path"], []).append(i)
    for lista in por.values():
        lista.sort(key=lambda i: (chunks[i]["_linea"], chunks[i]["id"]))
    return por


def vecinos(variante, i, chunks, por_archivo):
    c = chunks[i]
    hermanos = por_archivo.get(c["relative_path"], []) if c["_linea"] is not None else []
    if variante == "control" or not hermanos:
        return []
    if variante == "vecinos-1":
        pos = hermanos.index(i)
        return [hermanos[p] for p in (pos - 1, pos + 1) if 0 <= p < len(hermanos)]
    if variante == "clase":
        if c["_lenguaje"] != "CSharp" or not c["_clase"]:
            return []
        candidatos = [j for j in hermanos if j != i and chunks[j]["_clase"] == c["_clase"]]
    else:  # seccion-md
        if c["_lenguaje"] != "Markdown" or not c["_seccion"]:
            return []
        candidatos = [j for j in hermanos if j != i and chunks[j]["_seccion"] == c["_seccion"]]
    candidatos.sort(key=lambda j: (abs(chunks[j]["_linea"] - c["_linea"]), chunks[j]["_linea"], chunks[j]["id"]))
    return candidatos[:TOPE_GRUPO]


def ensamblar(variante, top, chunks, por_archivo):
    """Contexto final y conteos. El grupo se admite entero o no se admite (protocolo)."""
    partes, largo, en_contexto = [], 0, []
    hits_omitidos = grupos_descartados = 0
    for rango, i in enumerate(top, start=1):
        if i in en_contexto:
            continue  # ya entro como vecino de un hit anterior
        grupo = sorted({i, *vecinos(variante, i, chunks, por_archivo)} - set(en_contexto),
                       key=lambda j: (chunks[j]["_linea"] or 0, chunks[j]["id"]))
        entradas = [cabecera(chunks[j], rango) + "\n" + chunks[j]["content"] for j in grupo]
        tentativo, cabe = largo, True
        for e in entradas:
            if tentativo + len(e) + len(SEPARADOR) > MAX_CONTEXTO:  # mismo guard que BuildContextBlock
                cabe = False
                break
            tentativo += (len(SEPARADOR) if tentativo > 0 else 0) + len(e)
        if not cabe:
            hits_omitidos += 1
            grupos_descartados += len(grupo) > 1
            continue
        for e in entradas:
            if partes:
                partes.append(SEPARADOR)
            partes.append(e)
        largo = tentativo
        en_contexto.extend(grupo)
    return "".join(partes), en_contexto, hits_omitidos, grupos_descartados


def acierta(item, indices, chunks):
    """harness.evaluar_item, aplicado a un conjunto de chunks."""
    objetivos = item.get("SourceFiles") or ([item["SourceFile"]] if item.get("SourceFile") else [])
    anclas = item.get("TargetContentContains") or []
    if not objetivos or not anclas:
        return None
    obj = {o.lower() for o in objetivos}
    win = [chunks[i] for i in indices if os.path.basename((chunks[i]["relative_path"] or "").replace("\\", "/")).lower() in obj]
    return any(a in c["content"] for a in anclas for c in win)


def main():
    from tokenizers import Tokenizer

    tok = Tokenizer.from_file(str(next(TOKENIZADOR.iterdir()) / "tokenizer.json"))
    tok.no_truncation()  # tokenizer.json trae truncado a 512: el contexto entero debe contarse
    tok.no_padding()
    r111 = json.loads(RESULTADO_111.read_text(encoding="utf-8"))
    salida = {"protocolo": "docs/evaluaciones/1.23/protocolo.md", "max_contexto": MAX_CONTEXTO, "tope_grupo": TOPE_GRUPO,
              "variantes": VARIANTES, "colecciones": {}}
    for col, eval_set in COLECCIONES.items():
        ruta_es = RAG / "docs" / "eval" / eval_set
        sha = hashlib.sha256(ruta_es.read_bytes()).hexdigest()
        if sha != r111["colecciones"][col]["eval_set_sha256"]:
            sys.exit(f"{col}: el eval-set cambio desde 1.11 ({sha})")
        items = {"es": json.loads(ruta_es.read_text(encoding="utf-8")),
                 "en": json.loads((CACHE / "eval-en" / eval_set).read_text(encoding="utf-8"))}
        chunks = leer_jsonl(CACHE / f"chunks-{col}-c1024.jsonl")
        por_id = {c["id"]: i for i, c in enumerate(chunks)}
        estado = metadatos(col, chunks)
        por_archivo = indices_por_archivo(chunks)
        bloque = {"chunks": len(chunks), "metadatos": estado, "idiomas": {}}
        print(col, len(chunks), "chunks; posicion:", estado, flush=True)
        for idioma in ("es", "en"):
            preguntas = r111["colecciones"][col][f"denso-{idioma}"]["preguntas"]
            filas = []
            for n, (p, item) in enumerate(zip(preguntas, items[idioma])):
                if p["question"] != item["Question"]:
                    sys.exit(f"{col}/{idioma}: la pregunta {n} no coincide con el resultado de 1.11")
                top = [por_id[t] for t in p["top_ids"]]
                control_top10 = acierta(item, top, chunks)
                if control_top10 is None:
                    continue
                if control_top10 != p["hit_any_at_k"]["10"]:
                    sys.exit(f"{col}/{idioma}: hit@10 no reproduce 1.11 en '{item['Question']}'")
                fila = {"pregunta": n, "question": item["Question"], "hit10": control_top10, "variantes": {}}
                for v in VARIANTES:
                    texto, en_ctx, omitidos, descartados = ensamblar(v, top, chunks, por_archivo)
                    fila["variantes"][v] = {"ancla_en_contexto": acierta(item, en_ctx, chunks), "caracteres": len(texto),
                                            "tokens": len(tok.encode(texto, add_special_tokens=False).ids),
                                            "chunks": len(en_ctx), "hits_omitidos": omitidos, "grupos_descartados": descartados,
                                            "ids": [chunks[i]["id"] for i in en_ctx]}
                filas.append(fila)
            bloque["idiomas"][idioma] = filas
        salida["colecciones"][col] = bloque
    SALIDA.mkdir(parents=True, exist_ok=True)
    (SALIDA / "simulacion.json").write_text(json.dumps(salida, indent=1, ensure_ascii=False), encoding="utf-8")
    resumen(salida)


def resumen(salida):
    print()
    print(f"{'idioma':6} {'corpus':24} {'variante':11} {'n':>3} {'ancla':>5} {'gana':>4} {'pierde':>6} "
          f"{'car.medio':>9} {'tok.medio':>9} {'chunks':>6} {'omit.':>5} {'desc.':>5}")
    for idioma in ("es", "en"):
        for col in list(salida["colecciones"]) + ["TOTAL"]:
            filas = [f for c in salida["colecciones"] for f in salida["colecciones"][c]["idiomas"][idioma]
                     if col in ("TOTAL", c)]
            for v in VARIANTES:
                d = [f["variantes"][v] for f in filas]
                ctl = [f["variantes"]["control"]["ancla_en_contexto"] for f in filas]
                gana = sum(x["ancla_en_contexto"] and not c for x, c in zip(d, ctl))
                pierde = sum(c and not x["ancla_en_contexto"] for x, c in zip(d, ctl))
                print(f"{idioma:6} {col:24} {v:11} {len(d):>3} {sum(x['ancla_en_contexto'] for x in d):>5} {gana:>4} {pierde:>6} "
                      f"{sum(x['caracteres'] for x in d) / len(d):>9.0f} {sum(x['tokens'] for x in d) / len(d):>9.0f} "
                      f"{sum(x['chunks'] for x in d) / len(d):>6.1f} {sum(x['hits_omitidos'] for x in d):>5} "
                      f"{sum(x['grupos_descartados'] for x in d):>5}")
        print()


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "resumen":
        resumen(json.loads((SALIDA / "simulacion.json").read_text(encoding="utf-8")))
    else:
        main()
