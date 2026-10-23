"""Static audit: every menu item / button Tag in MainWindow.xaml must be handled by the code, and every XAML event handler must exist.
Exit code 0 = OK, 1 = problem. Usage: python tests/audit-menus.py"""
import glob
import os
import re
import sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
xaml = open(os.path.join(root, 'MainWindow.xaml'), encoding='utf-8-sig').read()
tags = sorted(set(re.findall(r'Tag="([^"]*)"', xaml)))
src = '\n'.join(open(f, encoding='utf-8-sig').read() for f in glob.glob(os.path.join(root, '*.cs')))
cases = set(re.findall(r'case "([^"]*)"', src))
prefixes = {p for p in re.findall(r'StartsWith\("([^"]*)"\)', src) if p}

problems = []
for t in tags:
    if t in cases or any(t.startswith(p) for p in prefixes):
        continue
    problems.append('menu/button Tag "%s" has no handler' % t)

events = re.findall(r'\b(?:Click|Drop|PreviewKeyDown|MouseLeftButtonDown|MouseWheel|Closing|MouseDoubleClick|MouseEnter|MouseLeave|ValueChanged|PreviewMouseLeftButtonDown|PreviewMouseLeftButtonUp)="([A-Za-z_]\w*)"', xaml)
for h in sorted(set(events)):
    if not re.search(r'\bvoid\s+' + h + r'\s*\(', src):
        problems.append('XAML event handler "%s" is missing in the code' % h)

print('%d menu/button tags and %d XAML handlers checked' % (len(tags), len(set(events))))
for p in problems:
    print('PROBLEM:', p)
sys.exit(1 if problems else 0)
