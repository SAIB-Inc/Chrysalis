#!/usr/bin/env python3
"""Guest guard; read its scoped token from IMDS in memory only."""
import fnmatch
import http.client
import json
import re
import subprocess
import sys
import time
import urllib.parse


def request(connection, verb, path, headers):
    try:
        connection.request(verb,path,headers=headers)
        response=connection.getresponse()
        return response.status,response.read(65536)
    finally:
        connection.close()


def imds(path):
    status,body=request(http.client.HTTPConnection('169.254.169.254',timeout=4),'GET',path,{'Metadata':'true'})
    if status!=200:
        raise ValueError('IMDS not ready')
    return json.loads(body)


def own_group(group,verb):
    identity=imds('/metadata/instance/compute?api-version=2021-02-01')
    sid=identity['subscriptionId']
    if identity['resourceGroupName'].lower()!=group or not re.fullmatch(r'[0-9a-f-]{36}',sid,re.I):
        raise ValueError('VM is not in the intended group')
    audience=urllib.parse.quote('https://management.azure.com/',safe='')
    # The VM carries exactly one user-assigned identity; no user token is used.
    token=imds('/metadata/identity/oauth2/token?api-version=2018-02-01&resource='+audience)['access_token']
    path=f'/subscriptions/{sid}/resourcegroups/{group}'
    headers = {'Authorization': 'Bearer ' + token}
    status, body = request(http.client.HTTPSConnection('management.azure.com', timeout=7), 'GET',
                           path + '?api-version=2021-04-01', headers)
    if status == 404:
        return status if verb != 'CHECK' else False
    if status != 200 or json.loads(body).get('tags', {}).get('chrysalisGate') != group:
        raise ValueError('group ownership not established')
    action='/providers/Microsoft.Authorization/permissions?api-version=2022-04-01' if verb=='CHECK' else '?api-version=2021-04-01'
    status,body=request(http.client.HTTPSConnection('management.azure.com',timeout=7),'GET' if verb=='CHECK' else verb,path+action,
                        headers)
    if verb=='CHECK':
        if status!=200:
            return False
        payload=json.loads(body)
        want='microsoft.resources/subscriptions/resourcegroups/delete'
        permits=lambda masks:any(fnmatch.fnmatchcase(want,mask.lower()) for mask in masks)
        return not payload.get('nextLink') and any(permits(r['actions']) and not permits(r['notActions']) for r in payload['value'])
    return status


def main(group,mode):
    if not re.fullmatch(r'chrysalis-gate-[0-9]{8}t[0-9]{6}-[a-f0-9]{8}',group):
        return 2
    if mode=='check':
        try:
            return 0 if own_group(group,'CHECK') else 1
        except Exception:
            return 1
    if mode=='delete':
        # Async acceptance is not proof. Re-submit if the guest survives. 404
        # (or this guest disappearing during delete) ends the sequence.
        stop=time.monotonic()+130
        while time.monotonic()<stop:
            try:
                status=own_group(group,'DELETE')
                if status==404:
                    return 0
            except Exception:
                pass
            time.sleep(7)
    if mode in ('delete','poweroff'):
        return subprocess.call(['/usr/bin/systemctl','--no-block','poweroff'])
    return 2


if __name__=='__main__':
    sys.exit(main(*sys.argv[1:]))
