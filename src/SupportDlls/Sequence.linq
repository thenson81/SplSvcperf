//--------------------------------------------------------------------------------
// SequenceItem SequenceDiagram[string name]   - Indexer to sequence steps         
// public void Connect(SequenceItem from, SequenceItem to, string message = null)
//--------------------------------------------------------------------------------            

var q = playback.GetObservable<SystemEvent>();
Guid root = Guid.Parse("39bd4d84-ca0a-4605-a23d-30fdfe27b1f6");
int activityDepth = 5;
int maxEvents = 100;

var  guids = new System.Collections.Generic.HashSet<Guid>();
guids.Add(root);
Func<Guid,Guid,bool> isValid = (activity, related) =>
            {
               if(guids.Count < activityDepth)
		{
                if (guids.Contains(activity))
                {
                    if (related != Guid.Empty)
                    {
			    guids.Add(related);			
                    }
                    return true;
                 }
		 }	
                return false;
            };

var t= from e in q where isValid(e.Header.ActivityId, e.Header.RelatedActivityId)
                 select new
        {
            Id = e.Header.EventId,
	     ActivityId = e.Header.ActivityId, 
	     RelatedActivityId = e.Header.RelatedActivityId
        };

var buffer = (from e in playback.BufferOutput(t) select e);
playback.Run();
var events = buffer.ToList();

SequenceDiagram diagram = new SequenceDiagram();
diagram.Title = "Http Activity";
var eventNames = (from s in playback.KnownTypes
                    let attr = (ManifestEventAttribute)s.GetCustomAttributes(false)
                                    .Where((e) => e is ManifestEventAttribute).FirstOrDefault()
                    where attr != null
                    select new
                    {
                        Id = attr.EventId,
                        //Opcode = attr.Opcode,
                        Name = s.Name
                    }).ToDictionary((e) => e.Id, (e) => e.Name);

int index = 1;

//var activities = (from e in events select e.ActivityId).Distinct().ToDictionary((e) => e, (e) => "Activity" + (index++));
var activities = (from e in guids select e).Distinct().ToDictionary((e) => e, (e) => "Activity" + (index++));
foreach(var values in activities.Values)
{
	var seq = new SequenceItem(values);
	diagram.Add(seq);
}

SequenceItem source = null;
SequenceItem to = null;

int count = 0;
foreach (var item in events)
{
    	if(count > maxEvents)
	{
		break;
	}
	string eventName = eventNames[item.Id];
	string name = activities[item.ActivityId];
    	source = diagram[name];
	if(item.RelatedActivityId != Guid.Empty)
	{
		diagram.Connect(source, 
				diagram[activities[item.RelatedActivityId]],
				eventName);
	}
	else
	{
		diagram.Connect(source,source,eventName);
        // To generate point events without connectors 
        // use null targets as shown below.
		//diagram.Connect(source,null,eventName);
	}
	count++;
}
diagram.Dump();
