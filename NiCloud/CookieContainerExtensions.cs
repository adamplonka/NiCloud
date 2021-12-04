using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

namespace NiCloud;

public static class CookieContainerExtensions
{
    private static readonly FieldInfo DomainTableField = typeof(CookieContainer)
        .GetField("m_domainTable", BindingFlags.Instance | BindingFlags.NonPublic);

    private static FieldInfo? PathListField;

    public static CookieCollection GetAllCookies(this CookieContainer container)
    {
        CookieCollection allCookies = new();
        var domainTable = (Hashtable)DomainTableField.GetValue(container);
        var pathLists = new List<SortedList>();
        lock (domainTable.SyncRoot)
        {
            foreach (var domain in domainTable.Values)
            {
                if (PathListField == null)
                {
                    PathListField = domain.GetType().GetField("m_list", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                var pathList = (SortedList)PathListField.GetValue(domain);
                pathLists.Add(pathList);
            }
        }

        foreach (var pathList in pathLists)
        {
            lock (pathList.SyncRoot)
            {
                foreach (CookieCollection cookies in pathList.GetValueList())
                {                        
                    allCookies.Add(cookies);
                }
            }
        }

        return allCookies;
    }
}
