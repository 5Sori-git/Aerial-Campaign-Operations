"""ACO 스크립트 C# 컴파일 검사 — Unity를 열지 않고, Unity가 마지막으로 컴파일할 때 쓴 설정 그대로 ACO 코드만 컴파일해 본다.

Unity가 남긴 응답 파일(Library/Bee/artifacts/*/Assembly-CSharp.rsp, Assembly-CSharp-Editor.rsp)에서 참조·정의·언어 버전·분석기를
가져오고, 소스만 Assets/ACO_Addon으로 바꾼다. 컴파일러도 Unity 것(Editor/Data/DotNetSdkRoslyn/csc.dll + NetCoreRuntime/dotnet).
  1) Assets/ACO_Addon/Scripts → 임시 Assembly-CSharp.dll
  2) Assets/ACO_Addon/Editor → 1)을 Assembly-CSharp 대신 참조
UdonSharp 고유 제약(지원 안 되는 문법·API)은 못 잡는다 — 그건 Unity에서 U# 컴파일러가 잡는다. 문법·타입·이름 오류를 먼저 잡는 용도.
Unity로 한 번 컴파일한 적이 있어야 한다(응답 파일이 있어야 한다). 패키지·Unity 버전을 바꿨으면 Unity로 다시 컴파일한 뒤 쓴다.

사용: python Tools/aco_compile_check.py
"""
import os, sys, glob, subprocess, tempfile, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def latest_rsp(name):
    files = glob.glob(os.path.join(ROOT, 'Library', 'Bee', 'artifacts', '*', name + '.rsp'))
    return max(files, key=os.path.getmtime) if files else None


def read_rsp(path):
    with open(path, encoding='utf-8-sig') as f:
        return [l.strip() for l in f if l.strip()]


def compiler_from(rsp_lines):
    """분석기 경로에서 Unity Editor 폴더를 찾아 그 안의 csc.dll / dotnet을 쓴다."""
    for l in rsp_lines:
        m = re.match(r'-analyzer:"(.+?)/Editor/Data/', l)
        if m:
            data = m.group(1) + '/Editor/Data'
            dotnet = os.path.join(data, 'NetCoreRuntime', 'dotnet.exe')
            csc = os.path.join(data, 'DotNetSdkRoslyn', 'csc.dll')
            if os.path.exists(dotnet) and os.path.exists(csc):
                return [dotnet, csc]
    return None


def build(rsp_lines, sources, out_dll, replace_refs, tmp, name):
    keep = []
    for l in rsp_lines:
        if l.startswith('-out:') or l.startswith('-refout:') or l.startswith('/out:'):
            continue
        if l.startswith('"') and l.endswith('.cs"'):
            continue   # 원래 소스
        if l.startswith('-r:'):
            base = os.path.basename(l[3:].strip('"'))
            # Assembly-CSharp.dll / Assembly-CSharp.ref.dll(참조 전용 출력) 모두 바꾼다
            key = base.replace('.ref.dll', '.dll')
            if key in replace_refs:
                l = '-r:"%s"' % replace_refs[key]
        keep.append(l)
    keep.append('-out:"%s"' % out_dll)
    keep.append('-preferreduilang:en-US')
    keep += ['"%s"' % s for s in sources]
    rsp = os.path.join(tmp, name + '.rsp')
    with open(rsp, 'w', encoding='utf-8') as f:
        f.write('\n'.join(keep))
    return rsp


def run(cmd, rsp):
    p = subprocess.run(cmd + ['@' + rsp], capture_output=True, text=True, encoding='utf-8', errors='replace', cwd=ROOT)
    out = p.stdout + p.stderr
    errors = [l for l in out.splitlines() if ': error ' in l or l.startswith('error ')]
    if p.returncode != 0 and not errors:
        errors = ['csc 종료 코드 %d: %s' % (p.returncode, out[:600])]
    return errors


def main():
    rt_rsp = latest_rsp('Assembly-CSharp')
    ed_rsp = latest_rsp('Assembly-CSharp-Editor')
    if not rt_rsp or not ed_rsp:
        print('Unity 컴파일 응답 파일(Library/Bee/artifacts/*/Assembly-CSharp*.rsp)이 없습니다 — Unity로 한 번 컴파일하세요.')
        return 2
    rt_lines = read_rsp(rt_rsp)
    cmd = compiler_from(rt_lines)
    if cmd is None:
        print('Unity 컴파일러(DotNetSdkRoslyn/csc.dll)를 찾지 못했습니다.')
        return 2
    tmp = tempfile.mkdtemp(prefix='aco_cc_')
    runtime = sorted(glob.glob(os.path.join(ROOT, 'Assets', 'ACO_Addon', 'Scripts', '**', '*.cs'), recursive=True))
    editor = sorted(glob.glob(os.path.join(ROOT, 'Assets', 'ACO_Addon', 'Editor', '**', '*.cs'), recursive=True))
    rt_dll = os.path.join(tmp, 'Assembly-CSharp.dll')
    errs = run(cmd, build(rt_lines, runtime, rt_dll, {}, tmp, 'rt'))
    print('런타임 %d파일: 오류 %d' % (len(runtime), len(errs)))
    for e in errs[:80]:
        print('  ' + e)
    if errs:
        return 1
    ed_lines = read_rsp(ed_rsp)
    errs = run(cmd, build(ed_lines, editor, os.path.join(tmp, 'Assembly-CSharp-Editor.dll'), {'Assembly-CSharp.dll': rt_dll}, tmp, 'ed'))
    print('에디터 %d파일: 오류 %d' % (len(editor), len(errs)))
    for e in errs[:80]:
        print('  ' + e)
    return 1 if errs else 0


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    sys.exit(main())
