// @ts-check
 
/**
@typedef {{
    GlobalRate: number,
    MinConRate: number,
    MaxConRate: number,
    AvgConRate: number
}} BandwithRate

@typedef {{
    Alive: boolean,
    StartupTime: number,
    GCMemory: number,
    Modules: number,
    TickRate: number,
    PlayerCounter: number,
    Registered: number,
    Banned: number,
    // Connections: number?,
    // Sessions: number?,
    // PlayersByCon: number?,
    // PlayersByID: number?,
    PlayerRefs: number,
    
    TCPUplinkBpS: BandwithRate, TCPUplinkPpS: BandwithRate,
    TCPDownlinkBpS: BandwithRate, TCPDownlinkPpS: BandwithRate,
    UDPUplinkBpS: BandwithRate, UDPUplinkPpS: BandwithRate,
    UDPDownlinkBpS: BandwithRate, UDPDownlinkPpS: BandwithRate
}} Status

@typedef {{
    ActivityRate : number,
    Role : string
}} NetPlusThread

@typedef {{
    Role : string,
    ActivityRate : number,
    NumThreads : number
}} NetPlusRole

@typedef {{
    PoolActivityRate : number,
    PoolNumThreads : number,
    PoolThreads : NetPlusThread[]?,
    PoolRoles : NetPlusRole[]?,
    SchedulerExecDuration : number,
    SchedulerNumThreadsReassigned : number,
    SchedulerNumThreadsIdled : number,
}} NetPlusStatus
 */
 
const { services } = require("netdata");
var netdata = require("netdata");
 
if (netdata.options.DEBUG === true)
    netdata.debug(`loaded plugin: ${__filename}`);
 
let CelesteNet = {
    name: __filename,
    enable_autodetect: true,
    update_every: 1,
    base_priority: 10100,
 
    charts: {},
 
    getChart: function(service, key, template) {
        const id = template.id || `${service.name}.${key}`;
        let chart = CelesteNet.charts[id];
        if (chart)
            return chart;
 
        chart = {
            id,
            name: template.name || "",
            title: template.title ? template.title.replace("$", service.name) : service.name,
            units: template.units,
            family: template.family || template.units,
            context: template.context || `${service.type}.${key}`,
            type: template.type || netdata.chartTypes.line,
            priority: service.base_priority + (template.priority || 0),
            update_every: service.update_every,
            dimensions: {},
        };
 
        for (let did in template.dimensions) {
            const dim = template.dimensions[did]
            chart.dimensions[did] = {
                id: did,
                name: dim.name || did,
                algorithm: dim.algorithm || netdata.chartAlgorithms.absolute,
                multiplier: dim.multiplier || 1,
                divisor: dim.divisor || 1,
                hidden: false,
            };
        }
 
        if (netdata.options.DEBUG === true)
            netdata.debug(`new chart: ${JSON.stringify(chart)}`);
        chart = service.chart(id, chart);
        CelesteNet.charts[id] = chart;
        return chart;
    },
 
    updateChart: function(service, key, template) {
        const chart = CelesteNet.getChart(service, key, template);
        if (netdata.options.DEBUG === true)
            netdata.debug(`updating chart ${service.name} ${key}`);
        service.begin(chart);
        for (let did in template.dimensions) {
            const dim = template.dimensions[did]
            if (netdata.options.DEBUG === true)
                netdata.debug(`updating value: ${did} = ${dim.value}`);
            service.set(did, dim.value);
        }
        service.end();
    },
 
    updateCharts: function(service, templates) {
        let i = 0;
        for (let tid in templates) {
            const template = templates[tid];
            if (!template.priority)
                template.priority = i++;
                CelesteNet.updateChart(service, tid, template);
        }
    },
 
    processResponse: function(service, dataRaw) {
        if (!dataRaw)
            return;
 
        if (service.type === "status") {
            if (netdata.options.DEBUG === true)
                netdata.debug(`received data for status: ${service.url} = ${dataRaw}`);
 
            /** @type {Status} */
            const data = JSON.parse(dataRaw);
 
            service.commit();
 
            var sCharts = {
                uptime: {
                    title: "$ uptime",
                    units: "seconds",
                    family: "uptime",
                    dimensions: {
                        time: {
                            value: (new Date().valueOf() - data.StartupTime) / 1000
                        },
                    },
                },
 
                gcmemory: {
                    title: "$ GC memory",
                    units: "bytes",
                    family: "memory",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        memory: {
                            value: data.GCMemory
                        },
                    },
                },

                tickRate: {
                    title: "$ tick rate",
                    units: "TpS",
                    family: "ticker",
                    dimensions: {
                        tickRate: {
                            value: data.TickRate * 1000,
                            divisor: 1000
                        },
                    },
                },
 
                counted: {
                    title: "$ player counter",
                    units: "players",
                    family: "players",
                    dimensions: {
                        players: {
                            value: data.PlayerCounter
                        },
                    },
                },
 
                registered: {
                    title: "$ players registered",
                    units: "players",
                    family: "players",
                    dimensions: {
                        players: {
                            value: data.Registered
                        },
                    },
                },
 
                banned: {
                    title: "$ players banned",
                    units: "players",
                    family: "players",
                    dimensions: {
                        players: {
                            value: data.Banned
                        },
                    },
                },
 
                online: {
                    title: "$ players online",
                    units: "players",
                    family: "players",
                    dimensions: {
                        players: {
                            value: data.PlayerRefs
                        },
                    },
                },

                tcpDownlinkBpS: {
                    title: "$ TCP BpS downlink",
                    units: "bytes per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.TCPDownlinkBpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.TCPDownlinkBpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.TCPDownlinkBpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.TCPDownlinkBpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                tcpDownlinkPpS: {
                    title: "$ TCP PpS downlink",
                    units: "packets per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.TCPDownlinkPpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.TCPDownlinkPpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.TCPDownlinkPpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.TCPDownlinkPpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                udpDownlinkBpS: {
                    title: "$ UDP BpS downlink",
                    units: "bytes per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.UDPDownlinkBpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.UDPDownlinkBpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.UDPDownlinkBpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.UDPDownlinkBpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                udpDownlinkPpS: {
                    title: "$ UDP PpS downlink",
                    units: "packets per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.UDPDownlinkPpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.UDPDownlinkPpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.UDPDownlinkPpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.UDPDownlinkPpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },
                
                tcpUplinkBpS: {
                    title: "$ TCP BpS uplink",
                    units: "bytes per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.TCPUplinkBpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.TCPUplinkBpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.TCPUplinkBpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.TCPUplinkBpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                udpUplinkBpS: {
                    title: "$ TCP PpS uplink",
                    units: "bytes per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.TCPUplinkPpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.TCPUplinkPpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.TCPUplinkPpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.TCPUplinkPpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                tcpUplinkPpS: {
                    title: "$ UDP TpS uplink",
                    units: "packets per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.UDPUplinkBpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.UDPUplinkBpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.UDPUplinkBpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.UDPUplinkBpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },

                udpUplinkPpS: {
                    title: "$ UDP PpS uplink",
                    units: "packets per second",
                    family: "bandwith",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        global: { value: data.UDPUplinkPpS.GlobalRate * 1000, divisor: 1000 },
                        minCon: { value: data.UDPUplinkPpS.MinConRate * 1000, divisor: 1000 },
                        maxCon: { value: data.UDPUplinkPpS.MaxConRate * 1000, divisor: 1000 },
                        avgCon: { value: data.UDPUplinkPpS.AvgConRate * 1000, divisor: 1000 }
                    }
                },
            };
            
            CelesteNet.updateCharts(service, sCharts);
        } else if (service.type === "netplus") {
            if (netdata.options.DEBUG === true)
                netdata.debug(`received data for netplus: ${service.url} = ${dataRaw}`);
 
            /** @type {NetPlusStatus} */
            const data = JSON.parse(dataRaw);
 
            service.commit();
 
            var nCharts = {
                poolActivity: {
                    title: "$ Total pool activity rate",
                    units: "percent",
                    family: "activity",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        activity: {
                            value: data.PoolActivityRate * 10000,
                            divisor: 100
                        },
                    },
                },
                schedulerExecDuration: {
                    title: "$ Last scheduler execution duration",
                    units: "ms",
                    family: "scheduler",
                    type: netdata.chartTypes.area,
                    dimensions: {
                        time: {
                            value: data.SchedulerExecDuration * 10000,
                            divisor: 100
                        },
                    },
                },
                schedulerNumThreadsReassigned: {
                    title: "$ Number of reassigned threads in last scheduler execution",
                    units: "threads",
                    family: "scheduler",
                    dimensions: {
                        numReassigned: {
                            value: data.SchedulerNumThreadsReassigned
                        },
                    },
                },
                schedulerNumThreadsIdled: {
                    title: "$ Number of threads sent ideling in last scheduler execution",
                    units: "threads",
                    family: "scheduler",
                    dimensions: {
                        numIdled: {
                            value: data.SchedulerNumThreadsIdled
                        },
                    },
                },
            }

            if (data.PoolThreads != null) {
                var threadActvDims = {}
                for (var i = 0; i < data.PoolThreads.length; i++) {
                    threadActvDims["Thread" + i] = { value: data.PoolThreads[i].ActivityRate * 10000, divisor: 100 };
                }
                nCharts["threadActivity"] = {
                    title: "$ Thread activity rates",
                    units: "percent",
                    family: "activity",
                    type: netdata.chartTypes.area,
                    dimensions: threadActvDims
                };
            }

            if (data.PoolRoles != null) {
                var roleActvDims = {}, roleThreadsDims = {}
                for (var i = 0; i < data.PoolRoles.length; i++) {
                    roleActvDims[data.PoolRoles[i].Role] = { value: data.PoolRoles[i].ActivityRate * 10000, divisor: 100 };
                    roleThreadsDims[data.PoolRoles[i].Role] = { value: data.PoolRoles[i].NumThreads };
                }
                nCharts["roleActivity"] = {
                    title: "$ Role activity rates",
                    units: "percent",
                    family: "activity",
                    type: netdata.chartTypes.area,
                    dimensions: roleActvDims
                };
                nCharts["roleThreadCount"] = {
                    title: "$ Role thread counts",
                    units: "threads",
                    family: "activity",
                    type: netdata.chartTypes.stacked,
                    dimensions: roleThreadsDims
                };
            }

            CelesteNet.updateCharts(service, nCharts);
        }
    },
 
    configure: function(config) {
        let added = 0;
 
        if (typeof(config.servers) !== "undefined") {
            for (let server of config.servers) {
                if (netdata.options.DEBUG === true)
                    netdata.debug(`adding server: ${JSON.stringify(server)}`);
                netdata.service({
                    name: server.name,
                    type: "status",
                    url: server.url + "/status",
                    request: netdata.requestFromURL(server.url + "/status"),
                    base_priority: this.base_priority + (server.priority || 0),
                    update_every: server.update_every || this.update_every,
                    module: this,
                }).execute(this.processResponse);
                netdata.service({
                    name: server.name,
                    type: "netplus",
                    url: server.url + "/netplus",
                    request: netdata.requestFromURL(server.url + "/netplus"),
                    base_priority: this.base_priority + (server.priority || 0),
                    update_every: server.update_every || this.update_every,
                    module: this,
                }).execute(this.processResponse);
                added++;
            }
        }
 
        if (netdata.options.DEBUG === true)
            netdata.debug(`added servers: ${added}`);
        return added;
    },
 
    update: function(service, callback) {
        if (netdata.options.DEBUG === true)
            netdata.debug(`update`);
        service.execute(function(serv, data) {
            if (netdata.options.DEBUG === true)
                netdata.debug(`execute`);
            service.module.processResponse(serv, data);
            if (netdata.options.DEBUG === true)
                netdata.debug(`callback`);
            callback();
            if (netdata.options.DEBUG === true)
                netdata.debug(`end?`);
        });
        if (netdata.options.DEBUG === true)
            netdata.debug(`end.`);
    },
};
 
module.exports = CelesteNet;
