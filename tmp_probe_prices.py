import json
import urllib.parse
import urllib.request
services = ['Virtual Machines','Storage','SQL Database','Backup','Azure Site Recovery']
base = 'https://prices.azure.com/api/retail/prices'
for service in services:
    url = base + '?$filter=' + urllib.parse.quote("serviceName eq '%s'" % service, safe='')
    print('SERVICE=' + service)
    try:
        with urllib.request.urlopen(url) as r:
            data = json.load(r)
        items = data.get('Items', [])
        print('RESULTS=' + str(len(items)))
        if items:
            for item in items[:5]:
                print('  serviceName={0} | productName={1} | meterName={2}'.format(item.get('serviceName'), item.get('productName'), item.get('meterName')))
        else:
            print('  NO_ITEMS')
    except Exception as e:
        print('ERROR=' + str(e))
    print('-' * 60)
