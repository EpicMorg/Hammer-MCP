"""Writes allowed_builds.h: registry builds that have an injectAdapter (sha256 of the editor DLL, exe, arch, adapter id).
The C# server (InjectPolicy) decides first; the native injector checks the same list again."""
import json, re, sys
src, out = sys.argv[1], sys.argv[2]
text = re.sub(r'^\s*//.*$', '', open(src, encoding='utf-8').read(), flags=re.M)
builds = [b for b in json.loads(text)['builds'] if b.get('injectAdapter')]
lines = ['// generated from registry/hammers.json by tools/gen_allowed_builds.py - do not edit', '#pragma once',
         'struct HmcpAllowedBuild { const char* id; const char* editorDll; const char* sha256; bool x64; const char* adapter; };',
         'static const HmcpAllowedBuild kHmcpAllowedBuilds[] = {']
for b in builds:
    lines.append(f'    {{ "{b["id"]}", "{b["match"]["dll"]}", "{b["match"]["sha256"].lower()}", {"true" if b["arch"] == "x64" else "false"}, "{b["injectAdapter"]}" }},')
lines += ['    { nullptr, nullptr, nullptr, false, nullptr },', '};', '']
import os
os.makedirs(os.path.dirname(out), exist_ok=True)
open(out, 'w', encoding='utf-8').write('\n'.join(lines))
print(f'{len(builds)} build(s) with an adapter')
