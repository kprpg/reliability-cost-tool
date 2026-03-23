import json
import urllib.parse
import urllib.request
services = ['Virtual Machines','Storage','SQL Database','Backup','Azure Site Recovery']
base = 'https://prices.azure.com/api/retail/prices'
for service in services:
    filt = urllib.parse.quote("serviceName eq '%s'" % service, safe='')
    url = base + '?$filter=' + filt + '&$top=5'
    print('SERVICE=' + service)
    try:
        req = urllib.request.Request(url, headers={'User-Agent':'Mozilla/5.0'})
        with urllib.request.urlopen(req, timeout=30) as r:
            data = json.load(r)
        items = data.get('Items', [])
        print('HAS_RESULTS=' + ('yes' if items else 'no'))
        if items:
            for item in items[:5]:
                print('  serviceName={0} | productName={1} | meterName={2}'.format(item.get('serviceName'), item.get('productName'), item.get('meterName')))
        else:
            print('  NO_ITEMS')
    except Exception as e:
        print('ERROR=' + str(e))
    print('-' * 60)
