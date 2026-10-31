#!/usr/bin/env python3
"""Developer knowledge inspection. No manual pass/fail injection is supported."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import sys
import knowledge
import navigator

def main(argv=None) -> int:
    p=argparse.ArgumentParser(description="Evidence-backed UI coverage and local QA history")
    p.add_argument("mode",choices=["coverage","qa"])
    p.add_argument("--repo-root",type=Path,default=Path(__file__).resolve().parents[2])
    p.add_argument("--feature",action="append",default=[])
    p.add_argument("--limit",type=int,default=25)
    p.add_argument("--query",default="")
    p.add_argument("--json",action="store_true")
    args=p.parse_args(argv)
    root=args.repo_root.resolve()
    try:
        features=navigator.features_from(root)
        if args.mode=="coverage":
            result=knowledge.discover_ui(navigator.build_index(root),features,args.limit,args.query)
        else:
            ids={f["id"] for f in features}
            requested=args.feature or sorted(ids)
            if any(x not in ids for x in requested):
                raise ValueError("Unknown feature ID")
            result={"source":knowledge.revision(root),
                    "features":knowledge.qa_status(root,requested),
                    "note":"Only local isolated tests are recorded. Installed-app UI is not verified."}
        if args.json:print(json.dumps(result,ensure_ascii=False,indent=2))
        elif args.mode=="coverage":
            print(f"XAML_MAPPED={result['knownHandlersMapped']};UNMAPPED_CANDIDATES={result['unmappedCandidates']};FILTER_MATCHES={result['matchedCandidates']}")
            for c in result["candidates"]:
                print(f"CANDIDATE {c['path']}:{c['line']} {c['name']} handlers={','.join(c['handlers'])}")
            print(result["note"])
        else:
            for id_,v in result["features"].items():
                print(f"FEATURE {id_} isolatedCurrent={v['isolatedChecksCurrent']} installedUi={v['installedAppUi']}")
                for x in v["records"]:
                    print(f"  {x['scope']} {x['test']} {x['status']} current={x['freshForCurrentCommit']}")
        return 0
    except (OSError, ValueError) as error:
        print("DEV_KNOWLEDGE_ERROR: "+str(error),file=sys.stderr)
        return 2

if __name__=="__main__":
    sys.exit(main())
