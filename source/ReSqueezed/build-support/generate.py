"""Generate translated game code locally from a supported user-supplied XBE."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

FUNCTION = re.compile(r"^void (sub_[0-9A-F]{8})\(void\)\n\{.*?^\}\n", re.MULTILINE | re.DOTALL)


def digest(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def apply_fixes(generated, recipe, support):
    pending = {entry["function"]: entry for entry in recipe["patches"]}
    for path in sorted(generated.glob("recomp_*.c")):
        text = path.read_text(encoding="utf-8")

        def patch(match):
            name = match[1]
            spec = pending.pop(name, None)
            if spec is None:
                return match[0]
            if digest(match[0]) != spec["before"]:
                raise RuntimeError(f"The pinned recompiler produced an unexpected body for {name}.")
            lines = match[0].splitlines(keepends=True)
            for edit in reversed(spec["edits"]):
                lines[edit["start"]:edit["end"]] = [edit["text"]]
            result = "".join(lines)
            if digest(result) != spec["after"]:
                raise RuntimeError(f"The compatibility patch failed verification for {name}.")
            return result

        text = FUNCTION.sub(patch, text)
        if FUNCTION.search(text):
            text = '#include "kernel.h"\n#include <stdio.h>\n#include <stdlib.h>\nextern int quit_confirmation;\nextern void xbox_Nv2aNotifyWork(void);\n' + text
        path.write_text(text, encoding="utf-8")
    if pending:
        raise RuntimeError(f"Missing functions for compatibility patches: {sorted(pending)}")
    # The dispatch list contains addresses only. Its runtime is ordinary host code.
    addresses = sorted(int(address, 16) for address in recipe["dispatch_addresses"])
    declarations = "".join(f"void sub_{address:08X}(void);\n" for address in addresses)
    header = generated / "recomp_funcs.h"
    text = header.read_text(encoding="utf-8")
    header.write_text(text.replace("#endif /* RECOMP_FUNCS_H */", declarations + "\n#endif /* RECOMP_FUNCS_H */"), encoding="utf-8")
    dispatch = ('#include "recomp_funcs.h"\n#include <stddef.h>\n#include <stdlib.h>\n'
                'typedef void (*recomp_func_t)(void);\n'
                'typedef struct { uint32_t xbox_va; recomp_func_t func; } recomp_entry_t;\n'
                'static const recomp_entry_t g_recomp_table[] = {\n')
    dispatch += "".join(f"    {{ 0x{address:08X}u, sub_{address:08X} }},\n" for address in addresses)
    dispatch += "};\n\n" + (support / "dispatch_runtime.c.in").read_text(encoding="utf-8")
    (generated / "recomp_dispatch.c").write_text(dispatch, encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--pipeline", type=Path, required=True)
    parser.add_argument("--scratch", type=Path, required=True)
    parser.add_argument("--existing-baseline", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    project = args.project.resolve()
    support = project / "build-support"
    recipe = json.loads((support / "patches.json").read_text(encoding="utf-8"))
    xbe = (args.game / "default.xbe").resolve()
    if hashlib.sha256(xbe.read_bytes()).hexdigest() != recipe["xbe_sha256"]:
        raise RuntimeError("Unsupported Juiced executable. Select the original USA / Europe retail release.")
    generated = project / "src/recomp/gen"
    if generated.exists():
        raise RuntimeError("The local generation folder already exists. Start a fresh build.")
    sys.path.insert(0, str(args.pipeline.resolve()))
    from tools.recomp import config
    from tools.recomp.translator import BatchTranslator
    config.configure_from_xbe(str(xbe))
    config.set_game_name("ReSqueezed")
    metadata = support / "metadata"
    print("Reading the supplied disc executable…", flush=True)
    batch = BatchTranslator(
        str(xbe), str(metadata / "functions.json"),
        labels_json_path=str(metadata / "labels.json"),
        identified_json_path=str(metadata / "identified_functions.json"),
        abi_json_path=str(metadata / "abi_functions.json"),
        output_dir=str(args.scratch.resolve()),
    )
    if args.existing_baseline:
        import shutil
        shutil.copytree(args.existing_baseline, generated)
    else:
        print("Generating the game's C code locally…", flush=True)
        functions = batch.get_functions_by_category()
        stats = batch.translate_batch_split(functions, output_dir=str(generated), chunk_size=250)
        if stats["failed"]:
            raise RuntimeError(f"Code generation failed for {stats['failed']} functions.")
    extra_code = ['#define RECOMP_GENERATED_CODE\n#include "kernel.h"\n#include "recomp_funcs.h"\n'
                  '#include <math.h>\nint quit_confirmation;\n']
    for extra in recipe["extra_functions"]:
        address = int(extra["start"], 16)
        info = dict(extra, end=int(extra["end"], 16), _addr=address)
        batch.translator.func_db[address] = info
        batch.translator.classification_db[address] = info
        batch.translator.abi_db[address] = extra["abi"]
        code = batch.translator.translate_function(address, info)
        body = next(FUNCTION.finditer(code))[0]
        if digest(body) != extra["expected_sha256"]:
            raise RuntimeError(f"Additional function {extra['name']} did not match the verified build.")
        extra_code.append(code)
    (generated / "recomp_extra.c").write_text("\n".join(extra_code), encoding="utf-8")
    apply_fixes(generated, recipe, support)
    bodies = {}
    for path in generated.glob("*.c"):
        for match in FUNCTION.finditer(path.read_text(encoding="utf-8")):
            if match[1] in bodies:
                raise RuntimeError(f"Duplicate generated function {match[1]}.")
            bodies[match[1]] = match[0]
    actual = digest("".join(name + bodies[name] for name in sorted(bodies)))
    if len(bodies) != recipe["expected_function_count"] or actual != recipe["expected_functions_sha256"]:
        raise RuntimeError("The generated code differs from the verified game build.")
    print(f"Verified {len(bodies):,} locally generated functions, including all compatibility fixes.", flush=True)


if __name__ == "__main__":
    main()
